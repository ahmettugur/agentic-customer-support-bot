// Services/Persistence/PostgresSessionManager.cs
// Hibrit cache + PostgreSQL oturum yöneticisi.
// ISessionManager implementasyonu — tamamen async (sync-over-async blocking yok).
//
// Davranış (in-memory ile aynı API ve semantik):
//   - Cache: ConcurrentDictionary<sessionId, AgentSession> + message history.
//   - AddExchange: cache + DB (UPSERT session, INSERT 2 message).
//   - AppendAssistantMessage: son boş assistant mesajını UPDATE veya INSERT.
//   - ExtractAndUpdateState: Mevcut in-memory mantık aynen — sonra UPSERT session.
//   - ClearSession: cache + DB DELETE (cascade → messages).
//   - GetAllSessions: cache (her session ilk kullanımda lazy hydrate).
//
// Basitlik için ExtractAndUpdateState içindeki regex/duygu/intent kuralları
// InMemorySessionManager'dan birebir kopyalandı (tek doğruluk kaynağı için
// ortak helper'a refactor Faz 4 cleanup'ında düşünülebilir).
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Session state (küçük, sınırlı boyut) Update() sonrası TAM olarak yayınlanır —
//     ChatModeRegistry/EscalationSink'teki desenle aynı.
//   - Mesaj geçmişi TAM listeyi değil, sadece DELTA'yı yayınlar (her mesajda büyüyen
//     bir listenin tamamını göndermek israf olurdu). Uzak pod bu session'ı daha önce
//     hiç görmediyse delta'yı yok sayar — ilk gerçek erişimde EnsureSessionHydrated
//     zaten DB'den TAM geçmişi çekip cache'i baştan kuracaktır (bkz. HydrateSessionAsync,
//     list.Clear() + DB'den yeniden doldurma), yani delta'dan doğan eksik/kısmi liste
//     kendi kendini onarır.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresSessionManager : ISessionManager
{
    private const string ChannelSessionUpdated = "csbot:session:updated";
    private const string ChannelHistoryChanged = "csbot:session:history";
    private const string ChannelSessionCleared = "csbot:session:cleared";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresSessionManager> _logger;
    private readonly IAppDistributedLock _distributedLock;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ConversationMessage>> _messageHistory = new();
    private readonly ConcurrentDictionary<string, byte> _hydratedSessions = new();
    private readonly SemaphoreSlim _allHydrationGate = new(1, 1);
    private volatile bool _allListHydrated;

    public PostgresSessionManager(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IAppDistributedLock distributedLock,
        IMessageBusPort messageBus,
        ILogger<PostgresSessionManager> logger)
    {
        _dbFactory = dbFactory;
        _distributedLock = distributedLock;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelSessionUpdated, OnRemoteSessionUpdated);
        _messageBus.Subscribe(ChannelHistoryChanged, OnRemoteHistoryChanged);
        _messageBus.Subscribe(ChannelSessionCleared, OnRemoteSessionCleared);
    }

    // ─── ISessionManager ───

    public async Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString();
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        if (_sessions.TryGetValue(sessionId, out var existing))
            return existing;

        var session = new AgentSession
        {
            SessionId = sessionId,
            CreatedAt = DateTime.Now,
            LastActivity = DateTime.Now,
            State = new SessionState()
        };

        if (_sessions.TryAdd(sessionId, session))
        {
            try { await UpsertSessionAsync(session).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] UPSERT (create) başarısız. Id={Id}", sessionId);
                throw ExceptionTranslator.Translate(ex, $"Session oluşturulamadı: {sessionId}");
            }
            return session;
        }

        // Eşzamanlı başka bir çağrı araya girdi — onun eklediği nesneyi kullan.
        return _sessions.TryGetValue(sessionId, out var winner) ? winner : session;
    }

    public async Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);
        return _sessions.TryGetValue(sessionId, out var s) ? s : null;
    }

    public async Task UpdateAsync(AgentSession session, CancellationToken ct = default)
    {
        session.LastActivity = DateTime.Now;
        _sessions[session.SessionId] = session;
        try { await UpsertSessionAsync(session).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] UPSERT başarısız. Id={Id}", session.SessionId);
            throw ExceptionTranslator.Translate(ex, $"Session güncellenemedi: {session.SessionId}");
        }
        PublishSessionUpdated(session);
    }

    public async Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureAllListHydratedAsync(ct).ConfigureAwait(false);
        return _sessions.Values.ToList();
    }

    public async Task ExtractAndUpdateStateAsync(
        string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        // Lock gerekmez — aynı session için aynı anda tek bot pipeline çalışır
        // (ConcurrentDictionary + in-memory cache). Sync-over-async lock pattern
        // thread pool starvation'a neden oluyordu.
        await ExtractAndUpdateStateCoreAsync(session, userMessage, botResponse, null, ct).ConfigureAwait(false);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        await using var handle = await _distributedLock
            .AcquireAsync($"session:{sessionId}", ct: ct)
            .ConfigureAwait(false);

        mutator(session.State);
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    private async Task ExtractAndUpdateStateCoreAsync(
        AgentSession session, string userMessage, string botResponse,
        IReadOnlyList<ConversationMessage>? priorHistory, CancellationToken ct,
        TurnSignals? signals = null)
    {
        // ConsecutiveNegativeTurns oku-değiştir-yaz içerdiği için kilitli: aynı session'a
        // çakışan iki eşzamanlı istek (çift-submit, çoklu sekme) birbirinin artışını ezerse
        // otomatik eskalasyon eşiği bir tur geç tetiklenir. GetOrCreateAsync/GetAsync aynı
        // sessionId için hep AYNI AgentSession referansını döndürdüğünden session nesnesi
        // kilit anahtarı olarak güvenlidir (bkz. WorkflowMessageBuilder.ConsumeForceReplanHint).
        lock (session)
        {
            SessionStateExtractor.ExtractAndApply(session.State, userMessage, botResponse, priorHistory, signals);
        }
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    // ─── Konuşma geçmişi ───

    public async Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);
        if (_messageHistory.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return new List<ConversationMessage>(history);
            }
        }
        return new List<ConversationMessage>();
    }

    public async Task AddExchangeAsync(
        string sessionId, string userQuery, string assistantResponse,
        TurnSignals? signals = null, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        List<ConversationMessage> priorHistorySnapshot;
        lock (history)
        {
            // Bu turdan ÖNCEKİ geçmiş — SessionStateExtractor'ın bağlam takibi için
            // (eklemeden önce alınmalı, yoksa "önceki tur" bu turun kendisi olur).
            priorHistorySnapshot = new List<ConversationMessage>(history);
            history.Add(new ConversationMessage(ConversationRoles.User, userQuery));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, assistantResponse));
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try { await InsertExchangeAsync(sessionId, userQuery, assistantResponse).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AddExchange INSERT başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Mesaj kaydedilemedi: {sessionId}");
        }
        PublishHistoryAppended(sessionId,
        [
            new ConversationMessage(ConversationRoles.User, userQuery),
            new ConversationMessage(ConversationRoles.Assistant, assistantResponse)
        ]);

        await ExtractAndUpdateStateCoreAsync(session, userQuery, assistantResponse, priorHistorySnapshot, ct, signals)
            .ConfigureAwait(false);
    }

    public async Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        bool replacedLastEmpty;
        lock (history)
        {
            replacedLastEmpty =
                history.Count > 0
                && history[^1].Role == ConversationRoles.Assistant
                && string.IsNullOrEmpty(history[^1].Text);

            if (replacedLastEmpty)
            {
                history[^1] = new ConversationMessage(ConversationRoles.Assistant, text);
            }
            else
            {
                history.Add(new ConversationMessage(ConversationRoles.Assistant, text));
            }
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try
        {
            await AppendAssistantMessageDbAsync(sessionId, text, replacedLastEmpty).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AppendAssistantMessage DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Assistant mesajı kaydedilemedi: {sessionId}");
        }

        var assistantMsg = new ConversationMessage(ConversationRoles.Assistant, text);
        if (replacedLastEmpty)
            PublishHistoryReplacedLast(sessionId, assistantMsg);
        else
            PublishHistoryAppended(sessionId, [assistantMsg]);
    }

    public async Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (history)
        {
            history.Add(new ConversationMessage(ConversationRoles.User, text));
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try
        {
            await AppendUserMessageDbAsync(sessionId, text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AppendUserMessage DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Kullanıcı mesajı kaydedilemedi: {sessionId}");
        }
        PublishHistoryAppended(sessionId, [new ConversationMessage(ConversationRoles.User, text)]);
    }

    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        _messageHistory.TryRemove(sessionId, out _);
        _hydratedSessions.TryRemove(sessionId, out _);

        try { await DeleteSessionAsync(sessionId).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] ClearSession DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Session silinemedi: {sessionId}");
        }
        PublishSessionCleared(sessionId);
    }

    public async Task<List<SessionInfo>> GetAllSessionsAsync(
        string? forCustomerId = null, CancellationToken ct = default)
    {
        await EnsureAllListHydratedAsync(ct).ConfigureAwait(false);

        var result = new List<SessionInfo>();
        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;

            // Müşteri kapsamı: yalnızca bu müşteriye BAĞLI oturumlar. Bağlanmamış (anonim)
            // oturumlar da elenir — başkasının başlattığı, henüz kimliğe bağlanmamış bir
            // oturumun kimliği sızmamalı.
            if (forCustomerId is not null &&
                !string.Equals(session.State.AuthenticatedCustomerId, forCustomerId, StringComparison.Ordinal))
                continue;

            var history = await GetHistoryAsync(kvp.Key, ct).ConfigureAwait(false);
            var firstUserMsg = history.FirstOrDefault(m => m.Role == ConversationRoles.User)?.Text;

            result.Add(new SessionInfo
            {
                SessionId = session.SessionId,
                Title = firstUserMsg is not null
                    ? (firstUserMsg.Length > 50 ? firstUserMsg[..50] + "..." : firstUserMsg)
                    : WellKnown.FallbackMessages.NewChat,
                LastActivity = session.LastActivity,
                MessageCount = history.Count
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DB IO
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertSessionAsync(AgentSession session)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == session.SessionId);
        var stateJson = JsonSerializer.Serialize(session.State);

        if (existing is null)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = session.SessionId,
                CreatedAt = session.CreatedAt.Kind == DateTimeKind.Utc ? session.CreatedAt : session.CreatedAt.ToUniversalTime(),
                LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime(),
                StateJson = stateJson
            });
        }
        else
        {
            existing.LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime();
            existing.StateJson = stateJson;
        }

        await ctx.SaveChangesAsync();
    }

    private async Task InsertExchangeAsync(string sessionId, string userQuery, string assistantResponse)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        // Session'ı garanti altına al (FK için)
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        var now = DateTime.UtcNow;
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Text = userQuery,
            CreatedAt = now
        });
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = assistantResponse,
            CreatedAt = now.AddMilliseconds(1)
        });

        await ctx.SaveChangesAsync();
    }

    private async Task AppendAssistantMessageDbAsync(string sessionId, string text, bool replaceLastEmpty)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        if (replaceLastEmpty)
        {
            // Aynı session'daki son assistant mesajı boş ise UPDATE
            var lastAssistant = await ctx.Messages
                .Where(m => m.SessionId == sessionId && m.Role == "assistant")
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            if (lastAssistant is not null && string.IsNullOrEmpty(lastAssistant.Text))
            {
                lastAssistant.Text = text;
                lastAssistant.CreatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
                return;
            }
            // Bulunmazsa INSERT'e düş.
        }

        // Session ensure
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = text,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AppendUserMessageDbAsync(string sessionId, string text)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Text = text,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task DeleteSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Cascade: session silinirse mesajlar otomatik silinir (FK).
        var session = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null) return;
        ctx.Sessions.Remove(session);
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hydration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Oturumu DB'den yeniden okur — gerekçe için bkz. <see cref="ISessionManager.ReloadAsync"/>.</summary>
    public async Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);

        var row = await ctx.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);

        // Kayıt yoksa (henüz yazılmamış yeni oturum) elimizdeki cache nesnesi geçerlidir.
        if (row is null)
            return await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);

        var state = string.IsNullOrWhiteSpace(row.StateJson) || row.StateJson == "{}"
            ? new SessionState()
            : JsonSerializer.Deserialize<SessionState>(row.StateJson) ?? new SessionState();

        var fresh = new AgentSession
        {
            SessionId = row.SessionId,
            CreatedAt = row.CreatedAt,
            LastActivity = row.LastActivity,
            State = state
        };

        _sessions[sessionId] = fresh;
        return fresh;
    }

    private async Task EnsureSessionHydratedAsync(string sessionId, CancellationToken ct)
    {
        if (_hydratedSessions.ContainsKey(sessionId)) return;
        if (!_hydratedSessions.TryAdd(sessionId, 0)) return;

        try { await HydrateSessionAsync(sessionId).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Flag'i geri al — aksi halde geçici bir DB hatası (timeout, deadlock) bu
            // session'ı process ömrü boyunca "hydrate edildi ama boş" olarak kalıcı hale
            // getirir; bir sonraki istek DB'yi tekrar denemeden geçmişsiz devam eder.
            _hydratedSessions.TryRemove(sessionId, out _);
            _logger.LogError(ex, "[Session] Hydrate başarısız. Id={Id}", sessionId);
        }
    }

    private async Task HydrateSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var sessionRow = await ctx.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (sessionRow is null) return;

        var state = string.IsNullOrWhiteSpace(sessionRow.StateJson) || sessionRow.StateJson == "{}"
            ? new SessionState()
            : JsonSerializer.Deserialize<SessionState>(sessionRow.StateJson) ?? new SessionState();

        _sessions[sessionId] = new AgentSession
        {
            SessionId = sessionRow.SessionId,
            CreatedAt = sessionRow.CreatedAt,
            LastActivity = sessionRow.LastActivity,
            State = state
        };

        var messages = await ctx.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var list = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (list)
        {
            list.Clear();
            foreach (var m in messages)
            {
                var role = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? ConversationRoles.User
                    : ConversationRoles.Assistant;
                list.Add(new ConversationMessage(role, m.Text));
            }
        }
    }

    private async Task EnsureAllListHydratedAsync(CancellationToken ct)
    {
        if (_allListHydrated) return;
        await _allHydrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_allListHydrated) return;
            await HydrateAllSessionMetadataAsync().ConfigureAwait(false);
            _allListHydrated = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Session] All-list hydrate başarısız.");
        }
        finally
        {
            _allHydrationGate.Release();
        }
    }

    private async Task HydrateAllSessionMetadataAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Sadece session metadata + ilk user message snippet alacağız.
        // Mesaj geçmişi tek tek lazy hydrate edilir (büyük session'larda gereksiz IO yapmamak için).
        var sessions = await ctx.Sessions.AsNoTracking()
            .OrderByDescending(s => s.LastActivity)
            .Take(500)
            .ToListAsync();

        foreach (var s in sessions)
        {
            if (_sessions.ContainsKey(s.SessionId)) continue;

            var state = string.IsNullOrWhiteSpace(s.StateJson) || s.StateJson == "{}"
                ? new SessionState()
                : JsonSerializer.Deserialize<SessionState>(s.StateJson) ?? new SessionState();

            _sessions[s.SessionId] = new AgentSession
            {
                SessionId = s.SessionId,
                CreatedAt = s.CreatedAt,
                LastActivity = s.LastActivity,
                State = state
            };
        }

        _logger.LogInformation("[Session] Metadata hydrate: {Count} session", sessions.Count);
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishSessionUpdated(AgentSession session)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId = session.SessionId,
            createdAt = session.CreatedAt,
            lastActivity = session.LastActivity,
            state = session.State
        };
        _messageBus.Publish(ChannelSessionUpdated, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteSessionUpdated(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            var state = JsonSerializer.Deserialize<SessionState>(root.GetProperty("state").GetRawText())
                ?? new SessionState();

            _sessions[sessionId] = new AgentSession
            {
                SessionId = sessionId,
                CreatedAt = root.GetProperty("createdAt").GetDateTime(),
                LastActivity = root.GetProperty("lastActivity").GetDateTime(),
                State = state
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteSessionUpdated parse hatası");
        }
    }

    /// <summary>
    /// Mesaj geçmişine DELTA yayınlar (tam listeyi değil) — bkz. dosya başındaki
    /// "Yatay ölçeklendirme" notu.
    /// </summary>
    private void PublishHistoryAppended(string sessionId, IReadOnlyList<ConversationMessage> messages)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId,
            op = "append",
            messages
        };
        _messageBus.Publish(ChannelHistoryChanged, JsonSerializer.Serialize(payload));
    }

    private void PublishHistoryReplacedLast(string sessionId, ConversationMessage message)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId,
            op = "replaceLast",
            messages = new[] { message }
        };
        _messageBus.Publish(ChannelHistoryChanged, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteHistoryChanged(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            var op = root.GetProperty("op").GetString();
            var incoming = JsonSerializer.Deserialize<List<ConversationMessage>>(
                root.GetProperty("messages").GetRawText()) ?? [];
            if (incoming.Count == 0) return;

            // Bu session bu pod'da hiç görülmediyse eklemiyoruz — kısmi/eksik bir liste
            // oluşturmak yerine ilk gerçek erişimde EnsureSessionHydrated'ın DB'den TAM
            // geçmişi çekmesine bırakıyoruz.
            if (!_messageHistory.TryGetValue(sessionId, out var history)) return;

            lock (history)
            {
                if (op == "replaceLast" && history.Count > 0
                    && history[^1].Role == ConversationRoles.Assistant)
                {
                    history[^1] = incoming[0];
                }
                else
                {
                    history.AddRange(incoming);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteHistoryChanged parse hatası");
        }
    }

    private void PublishSessionCleared(string sessionId)
    {
        var payload = new { nodeId = _messageBus.NodeId, sessionId };
        _messageBus.Publish(ChannelSessionCleared, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteSessionCleared(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            _sessions.TryRemove(sessionId, out _);
            _messageHistory.TryRemove(sessionId, out _);
            _hydratedSessions.TryRemove(sessionId, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteSessionCleared parse hatası");
        }
    }
}
