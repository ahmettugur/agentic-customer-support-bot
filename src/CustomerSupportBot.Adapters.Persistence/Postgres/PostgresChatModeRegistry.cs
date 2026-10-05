// Services/Persistence/PostgresChatModeRegistry.cs
// HITL Live Takeover — hibrit cache + PostgreSQL kip registry + Redis pub/sub.
//
// Davranış:
//   - In-memory ConcurrentDictionary state'i + ModeChanged event aynen korunur.
//   - TakeOver/Release güncellemeleri DB'ye write-through.
//   - Cache lazy hydrate: ilk erişimde DB'deki tüm kayıtlar yüklenir.
//   - Singleton servis ⇒ DbContext IDbContextFactory ile açılır.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - TakeOver/Release sonrası csbot:chatmode kanalına yayın yapılır.
//   - Uzak pod'lar lokal state'lerini günceller ve ModeChanged event'ini tetikler.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresChatModeRegistry : IChatModeRegistry, ICacheWarmup, ISessionDataEraser
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresChatModeRegistry> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly IAppDistributedLock _distributedLock;
    private readonly ConcurrentDictionary<string, ChatSessionState> _states = new();
    private readonly SemaphoreSlim _hydrationGate = new(1, 1);
    private volatile bool _hydrated;

    public event EventHandler<ChatSessionState>? ModeChanged;

    public PostgresChatModeRegistry(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        IAppDistributedLock distributedLock,
        ILogger<PostgresChatModeRegistry> logger)
    {
        _dbFactory = dbFactory;
        _distributedLock = distributedLock;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe("csbot:chatmode", OnRemoteModeChanged);
        PrivacyChannels.SubscribeSessionsErased(_messageBus, EvictSessions, _logger);
    }

    // ─── Kişisel veri silme (ISessionDataEraser) ───

    public string Name => "chat-modes";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        var ids = sessionIds.ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var deleted = await db.ChatSessionModes.Where(m => ids.Contains(m.SessionId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        EvictSessions(ids.ToHashSet(StringComparer.Ordinal));
        PrivacyChannels.PublishSessionsErased(_messageBus, ids);
        return deleted;
    }

    private void EvictSessions(IReadOnlySet<string> sessionIds)
    {
        foreach (var id in sessionIds) _states.TryRemove(id, out _);
    }

    public ChatMode GetMode(string sessionId)
    {
        EnsureHydrated();
        return _states.TryGetValue(sessionId, out var s) ? s.Mode : ChatMode.Bot;
    }

    public ChatSessionState? GetState(string sessionId)
    {
        EnsureHydrated();
        return _states.TryGetValue(sessionId, out var s) ? s : null;
    }

    public async Task<bool> TakeOverAsync(string sessionId, string? humanAgent)
{
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        await EnsureHydratedAsync().ConfigureAwait(false);

        var agent = humanAgent ?? WellKnown.Defaults.Admin;

        // Distributed lock: farklı pod'lardan eş zamanlı await TakeOver() çağrılarını serialize eder.
        // Aynı session için yalnızca bir admin devralabilir.
        var handle = await _distributedLock.TryAcquireAsync($"takeover:{sessionId}").ConfigureAwait(false);
        if (handle is null)
        {
            _logger.LogWarning(
                "[HITL] TakeOver distributed lock alınamadı; reddedildi. Session={Session}, Agent={Agent}",
                sessionId, agent);
            return false;
        }

        try
        {
            // Lock altında güncel durumu DB'DEN OKU — başka pod çoktan devralmış olabilir.
            // Yerel cache'e bakmak yetmez: devralma bilgisi bu pod'a Redis pub/sub ile ulaşır
            // ve o mesaj kaybolabilir. Mesajı kaçıran pod cache'inde hiçbir sahip görmez,
            // devralmayı kabul eder ve koşulsuz UPSERT ile mevcut admin'i EZER. Kilit bunu
            // engellemez — kilit yalnızca eş zamanlı çağrıları sıraya sokar, sonradan gelen
            // bayat bir kararı değil.
            var current = await ReadStateFromDbAsync(sessionId).ConfigureAwait(false);
            if (current is not null
                && current.Mode == ChatMode.Human
                && !string.Equals(current.HumanAgent, agent, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[HITL] TakeOver reddedildi: session={Session} zaten {Existing} tarafından alındı. İstekte bulunan: {Requester}",
                    sessionId, current.HumanAgent, agent);
                return false;
            }

            var state = _states.AddOrUpdate(
                sessionId,
                _ => new ChatSessionState
                {
                    SessionId = sessionId,
                    Mode = ChatMode.Human,
                    HumanAgent = agent,
                    EnteredAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow
                },
                (_, existing) =>
                {
                    existing.Mode = ChatMode.Human;
                    existing.HumanAgent = agent;
                    existing.EnteredAt ??= DateTime.UtcNow;
                    existing.LastActivityAt = DateTime.UtcNow;
                    return existing;
                });

            try { await UpsertAsync(state).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] TakeOver DB UPSERT başarısız. Session={Session}", sessionId);
                throw;
            }

            _logger.LogInformation(
                "[HITL] TakeOver: session={Session}, agent={Agent}",
                sessionId, state.HumanAgent);

            FireChanged(state);
            PublishRedis(state);
            return true;
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Oturumu insan modundan çıkarır.
    /// </summary>
    /// <remarks>
    /// <see cref="TakeOverAsync"/> ile AYNI kilidi alır ve kararını DB'den okunan durumla verir.
    /// Eskiden ikisini de yapmıyordu: kilitsiz olduğu için bir devralma ile yarışabiliyor,
    /// bayat cache'ten karar verdiği için de Redis mesajını kaçırmış bir pod'da başka bir
    /// admin'in aktif oturumunu serbest bırakabiliyordu.
    /// </remarks>
    public async Task<bool> ReleaseAsync(string sessionId)
{
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        await EnsureHydratedAsync().ConfigureAwait(false);

        var handle = await _distributedLock.TryAcquireAsync($"takeover:{sessionId}").ConfigureAwait(false);
        if (handle is null)
        {
            _logger.LogWarning(
                "[HITL] Release distributed lock alınamadı; reddedildi. Session={Session}", sessionId);
            return false;
        }

        try
        {
            var state = await ReadStateFromDbAsync(sessionId).ConfigureAwait(false);
            if (state is null || state.Mode == ChatMode.Bot) return false;

            state.Mode = ChatMode.Bot;
            state.HumanAgent = null;
            state.EnteredAt = null;
            state.LastActivityAt = DateTime.UtcNow;
            _states[sessionId] = state;

            try { await UpsertAsync(state).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Release DB UPSERT başarısız. Session={Session}", sessionId);
                throw;
            }

            _logger.LogInformation("[HITL] Release: session={Session}", sessionId);
            FireChanged(state);
            PublishRedis(state);
            return true;
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Oturum durumunu <b>kayıtların gerçek kaynağından</b> okur ve yerel cache'i tazeler.
    /// Satır yoksa <c>null</c> döner. Sahiplik kararları bunun üzerinden verilmelidir.
    /// </summary>
    private async Task<ChatSessionState?> ReadStateFromDbAsync(string sessionId)
    {
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var row = await ctx.ChatSessionModes.AsNoTracking()
                .FirstOrDefaultAsync(m => m.SessionId == sessionId).ConfigureAwait(false);
            if (row is null) return null;

            var state = new ChatSessionState
            {
                SessionId = row.SessionId,
                Mode = Enum.TryParse<ChatMode>(row.Mode, ignoreCase: true, out var m) ? m : ChatMode.Bot,
                HumanAgent = row.HumanAgent,
                EnteredAt = row.EnteredAt,
                LastActivityAt = row.LastActivityAt,
                MessageCount = row.MessageCount
            };

            _states[sessionId] = state;
            return state;
        }
        catch (Exception ex)
        {
            // DB okunamıyorsa cache'e DÜŞMÜYORUZ: bayat cache üzerinden verilen bir sahiplik
            // kararı tam da bu düzeltmenin kapattığı hatadır. Okuyamamak, "sahip yok"
            // anlamına gelmez.
            _logger.LogError(ex, "[HITL] Sahiplik durumu DB'den okunamadı. Session={Session}", sessionId);
            throw;
        }
    }

    public IReadOnlyList<ChatSessionState> GetActive()
    {
        EnsureHydrated();
        return _states.Values
            .Where(s => s.Mode == ChatMode.Human)
            .OrderByDescending(s => s.EnteredAt ?? DateTime.MinValue)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(ChatSessionState state)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var existing = await ctx.ChatSessionModes
            .FirstOrDefaultAsync(m => m.SessionId == state.SessionId);

        if (existing is null)
        {
            ctx.ChatSessionModes.Add(new ChatSessionModeEntity
            {
                SessionId = state.SessionId,
                Mode = state.Mode.ToString(),
                HumanAgent = state.HumanAgent,
                EnteredAt = state.EnteredAt,
                LastActivityAt = state.LastActivityAt,
                MessageCount = state.MessageCount
            });
        }
        else
        {
            existing.Mode = state.Mode.ToString();
            existing.HumanAgent = state.HumanAgent;
            existing.EnteredAt = state.EnteredAt;
            existing.LastActivityAt = state.LastActivityAt;
            existing.MessageCount = state.MessageCount;
        }

        await ctx.SaveChangesAsync();
    }

    /// <inheritdoc />
    public Task WarmUpAsync(CancellationToken ct = default) => EnsureHydratedAsync();

    /// <summary>
    /// Senkron okuma yolları için YEDEK. Normalde cache açılışta <see cref="WarmUpAsync"/> ile
    /// doldurulmuştur ve bu çağrı bayrağı okuyup hemen döner; yalnızca ısıtma başarısız
    /// olduysa ilk okuma hydrate'i senkron bekler.
    /// </summary>
    private void EnsureHydrated()
    {
        if (_hydrated) return;
        EnsureHydratedAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureHydratedAsync()
    {
        if (_hydrated) return;
        await _hydrationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_hydrated) return;
            await HydrateAsync().ConfigureAwait(false);
            _hydrated = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] ChatMode cache hydrate başarısız.");
        }
        finally
        {
            _hydrationGate.Release();
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.ChatSessionModes.AsNoTracking().ToListAsync();

        foreach (var e in rows)
        {
            var mode = Enum.TryParse<ChatMode>(e.Mode, ignoreCase: true, out var m)
                ? m : ChatMode.Bot;

            _states[e.SessionId] = new ChatSessionState
            {
                SessionId = e.SessionId,
                Mode = mode,
                HumanAgent = e.HumanAgent,
                EnteredAt = e.EnteredAt,
                LastActivityAt = e.LastActivityAt,
                MessageCount = e.MessageCount
            };
        }

        _logger.LogInformation("[HITL] ChatMode cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private void FireChanged(ChatSessionState state)
    {
        try { ModeChanged?.Invoke(this, state); }
        catch (Exception ex) { _logger.LogWarning(ex, "ModeChanged handler failed"); }
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void OnRemoteModeChanged(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            var modeStr = root.GetProperty("mode").GetString() ?? "Bot";
            var mode = Enum.TryParse<ChatMode>(modeStr, ignoreCase: true, out var m) ? m : ChatMode.Bot;

            var state = _states.AddOrUpdate(
                sessionId,
                _ => new ChatSessionState { SessionId = sessionId, Mode = mode },
                (_, existing) =>
                {
                    existing.Mode = mode;
                    existing.HumanAgent = root.TryGetProperty("humanAgent", out var ha) && ha.ValueKind != JsonValueKind.Null ? ha.GetString() : null;
                    existing.EnteredAt = root.TryGetProperty("enteredAt", out var ea) && ea.ValueKind != JsonValueKind.Null ? ea.GetDateTime() : null;
                    existing.LastActivityAt = root.TryGetProperty("lastActivityAt", out var la) && la.ValueKind != JsonValueKind.Null ? la.GetDateTime() : null;
                    return existing;
                });

            if (state.HumanAgent is null && root.TryGetProperty("humanAgent", out var haProp) && haProp.ValueKind != JsonValueKind.Null)
                state.HumanAgent = haProp.GetString();
            if (root.TryGetProperty("enteredAt", out var eaProp) && eaProp.ValueKind != JsonValueKind.Null)
                state.EnteredAt = eaProp.GetDateTime();
            if (root.TryGetProperty("lastActivityAt", out var laProp) && laProp.ValueKind != JsonValueKind.Null)
                state.LastActivityAt = laProp.GetDateTime();

            FireChanged(state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteModeChanged parse hatası");
        }
    }

    private void PublishRedis(ChatSessionState state)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId = state.SessionId,
            mode = state.Mode.ToString(),
            humanAgent = state.HumanAgent,
            enteredAt = state.EnteredAt,
            lastActivityAt = state.LastActivityAt
        };
        _messageBus.Publish("csbot:chatmode", JsonSerializer.Serialize(payload));
    }
}

