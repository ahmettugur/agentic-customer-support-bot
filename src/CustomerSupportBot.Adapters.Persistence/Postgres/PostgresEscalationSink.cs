// Services/Persistence/PostgresEscalationSink.cs
// HITL — Hibrit cache + PostgreSQL eskalasyon kuyruğu + Redis pub/sub.
//
// Davranış:
//   - In-memory dictionary + RequestCreated/RequestDecided event'leri korunur.
//   - Create: DB'ye INSERT + cache'e ekle + event fire. Session + ajan başına tek açık
//     eskalasyon DB'deki unique filtered index ile garanti edilir; yarışı kaybeden çağrı
//     mevcut kaydı geri alır (yeni kayıt/event yok).
//   - Decide: state machine (EscalationStateFactory) cache nesnesinin KOPYASI üzerinde
//     çalışır; DB'ye koşullu UPDATE (WHERE status = beklenen) yazılır. Yarışı kaybeden
//     karar false döner ve cache DB'den tazelenir.
//   - Cache lazy hydrate: son N kayıt yüklenir.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Create/Decide sonrası Redis'e yayın yapılır.
//   - Uzak pod'lar cache'i günceller ve lokal event'leri tetikler.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresEscalationSink : IEscalationSink, ICacheWarmup, ISessionDataEraser
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresEscalationSink> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, EscalationRequest> _byId = new();
    private readonly SemaphoreSlim _hydrationGate = new(1, 1);
    private volatile bool _hydrated;

    private const int HydrateRecentCount = 500;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    public PostgresEscalationSink(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresEscalationSink> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _messageBus = messageBus;
        _messageBus.Subscribe("csbot:escalation:created", OnRemoteCreated);
        _messageBus.Subscribe("csbot:escalation:decided", OnRemoteDecided);
        PrivacyChannels.SubscribeSessionsErased(_messageBus, ScrubOrEvictSessions, _logger);
    }

    // ─── Kişisel veri silme (ISessionDataEraser) ───

    public string Name => "escalations";

    /// <summary>Açık eskalasyonda silinen müşteri metninin yerine konan not.</summary>
    internal const string ErasedText = "[Kişisel veri silindi]";

    /// <summary>
    /// Kapanmış (çözülmüş/geçersiz) eskalasyonlar silinir. Açık ya da alınmış olanlar SİLİNMEZ: temsilci
    /// kuyruğunda ve yük sayaçlarında yer alırlar; kayıt kalkarsa atanan temsilcinin yükü hiç düşmezdi.
    /// Onların müşteri metni (soru, özet, çözüm notu, eksik bilgi listesi) temizlenir.
    /// </summary>
    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        var ids = sessionIds.ToList();
        var closed = new[] { nameof(EscalationStatus.Resolved), nameof(EscalationStatus.Dismissed) };
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var deleted = await db.Escalations
            .Where(e => e.SessionId != null && ids.Contains(e.SessionId) && closed.Contains(e.Status))
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var scrubbed = await db.Escalations
            .Where(e => e.SessionId != null && ids.Contains(e.SessionId) && !closed.Contains(e.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.UserQuery, ErasedText)
                .SetProperty(e => e.ResponseSummary, (string?)null)
                .SetProperty(e => e.Resolution, (string?)null)
                .SetProperty(e => e.MissingContextJson, "[]"), ct).ConfigureAwait(false);

        ScrubOrEvictSessions(ids.ToHashSet(StringComparer.Ordinal));
        PrivacyChannels.PublishSessionsErased(_messageBus, ids);
        return deleted + scrubbed;
    }

    private void ScrubOrEvictSessions(IReadOnlySet<string> sessionIds)
    {
        foreach (var e in _byId.Values)
        {
            if (e.SessionId is null || !sessionIds.Contains(e.SessionId)) continue;
            if (e.Status is EscalationStatus.Resolved or EscalationStatus.Dismissed)
            {
                _byId.TryRemove(e.Id, out _);
                continue;
            }
            // Önbellekteki nesneyi yerinde değiştirmek yerine kopya konur: okuyucular eski nesneyi
            // tutuyor olabilir (bkz. DecideAsync — durum makinesi de kopya üzerinde çalışır).
            var scrubbed = Clone(e);
            scrubbed.UserQuery = ErasedText;
            scrubbed.ResponseSummary = null;
            scrubbed.Resolution = null;
            scrubbed.MissingContext = [];
            _byId[e.Id] = scrubbed;
        }
    }

    public async Task<EscalationRequest> CreateAsync(EscalationRequest request)
{
        await EnsureHydratedAsync().ConfigureAwait(false);

        try { await InsertAsync(request).ConfigureAwait(false); }
        catch (Exception ex) when (IsOpenDedupViolation(ex))
        {
            // Yarışı başka bir çağrı (paralel alt görev ya da başka bir pod) kazandı: aynı
            // session+ajan için açık bir eskalasyon zaten var. Mükerrer kayıt oluşmaz; kazanan
            // döndürülür. Çağıran, döndürülen Id'nin kendi Id'si olup olmadığına bakarak
            // kaydın yeni oluşup oluşmadığını anlar.
            var existing = await FindOpenAsync(request.SessionId, request.AgentName).ConfigureAwait(false);
            if (existing is not null)
            {
                _byId.TryAdd(existing.Id, existing);
                _logger.LogInformation(
                    "[HITL] Mükerrer eskalasyon DB kısıtınca engellendi — mevcut kayıt kullanılıyor. "
                  + "session={Session} agent={Agent} existingId={ExistingId}",
                    request.SessionId, request.AgentName, existing.Id);
                return existing;
            }

            // Kısıt ihlal edildi ama açık kayıt artık yok — tam bu sırada kapatılmış olabilir.
            _logger.LogWarning(ex,
                "[HITL] Escalation INSERT dedup kısıtına takıldı ama açık kayıt bulunamadı. Id={Id}",
                request.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Escalation INSERT başarısız. Id={Id}", request.Id);
            throw;
        }

        _byId[request.Id] = request;

        _logger.LogWarning(
            "[HITL] Escalation created: id={Id}, agent={Agent}, reason={Reason}",
            request.Id, request.AgentName, request.Reason);

        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

        PublishRedis("csbot:escalation:created", request);
        return request;
    }

    public IReadOnlyList<EscalationRequest> GetOpen()
    {
        EnsureHydrated();
        return _byId.Values
            .Where(e => e.Status == EscalationStatus.Open || e.Status == EscalationStatus.Acknowledged)
            .OrderBy(e => e.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Agent kapsamlı geçmiş — <b>doğrudan veritabanından</b>, cache'ten değil.
    ///
    /// <para>
    /// Cache yalnızca açık kayıtlar + son <see cref="HydrateRecentCount"/> kapalı kaydı tutar.
    /// Bir agent'ın kendi kapalı eskalasyonu o pencerenin gerisinde kalabilir; cache üzerinden
    /// filtrelemek onu görünmez bırakırdı. Hem <c>assigned_to</c> daraltması hem de limit
    /// burada, tek bir SQL sorgusunda uygulanır.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);

        var rows = await ctx.Escalations.AsNoTracking()
            .Where(e => e.AssignedTo == null || e.AssignedTo == agentId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public IReadOnlyList<EscalationRequest> GetRecent(int count = 50)
    {
        EnsureHydrated();
        return _byId.Values
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList();
    }

    public EscalationRequest? Get(string id)
    {
        EnsureHydrated();
        return _byId.TryGetValue(id, out var e) ? e : null;
    }

    public async Task<bool> DecideAsync(string id, string action, string? assignedTo = null, string? resolution = null)
{
        await EnsureHydratedAsync().ConfigureAwait(false);
        if (!_byId.TryGetValue(id, out var cached)) return false;

        // Geçiş cache'teki nesnenin KOPYASI üzerinde hesaplanır. Eskiden state machine paylaşılan
        // nesneyi doğrudan değiştiriyor, ardından DB koşulsuz UPDATE ediliyordu: aynı kaydı aynı
        // anda karara bağlayan iki çağrı (iki admin, iki pod) ikisi de "Open" görüp ikisi de
        // başarılı sayılıyor, sonra yazan öncekinin kararını eziyordu.
        var expectedStatus = cached.Status;
        var req = Clone(cached);

        var normalized = (action ?? "").Trim().ToLowerInvariant();
        var state = EscalationStateFactory.Create(req.Status);

        var success = normalized switch
        {
            WellKnown.EscalationActions.Acknowledge
                or WellKnown.EscalationActions.Ack => state.Acknowledge(req, assignedTo),
            WellKnown.EscalationActions.Resolve => state.Resolve(req, assignedTo, resolution),
            WellKnown.EscalationActions.Dismiss => state.Dismiss(req, assignedTo, resolution),
            _ => false
        };

        if (!success) return false;

        bool applied;
        try { applied = await TryApplyDecisionAsync(req, expectedStatus).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Escalation UPDATE başarısız. Id={Id}", id);
            throw;
        }

        if (!applied)
        {
            _logger.LogInformation(
                "[HITL] Escalation kararı uygulanmadı — kayıt bu arada değişmiş. Id={Id} beklenen={Expected}",
                id, expectedStatus);
            return false;
        }

        _byId[id] = req;

        _logger.LogInformation(
            "[HITL] Escalation decided: id={Id}, action={Action}, status={Status}",
            id, normalized, req.Status);

        try { RequestDecided?.Invoke(this, req); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

        PublishRedis("csbot:escalation:decided", req);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task InsertAsync(EscalationRequest req)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.Escalations.Add(ToEntity(req));
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Kararı yalnızca kayıt hâlâ <paramref name="expectedStatus"/> durumundaysa yazar
    /// (koşullu UPDATE — optimistic concurrency). Aynı kararı yarışan ikinci çağrı 0 satır
    /// günceller ve <c>false</c> alır; böylece iki admin/pod'un ikisi de "başarılı" sayılıp
    /// birbirinin kararını ezemez.
    /// </summary>
    private async Task<bool> TryApplyDecisionAsync(EscalationRequest req, EscalationStatus expectedStatus)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var expected = expectedStatus.ToString();
        var newStatus = req.Status.ToString();

        var updated = await ctx.Escalations
            .Where(e => e.Id == req.Id && e.Status == expected)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, newStatus)
                .SetProperty(e => e.AcknowledgedAt, req.AcknowledgedAt)
                .SetProperty(e => e.ResolvedAt, req.ResolvedAt)
                .SetProperty(e => e.AssignedTo, req.AssignedTo)
                .SetProperty(e => e.Resolution, req.Resolution));

        if (updated == 1) return true;

        // 0 satır: ya kayıt DB'de yok ya da durumu başka biri tarafından değiştirildi.
        var current = await ctx.Escalations.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.Id);

        if (current is null)
        {
            // Cache'de var, DB'de yok — beklenmez ama dirence INSERT yapalım (eski davranış).
            ctx.Escalations.Add(ToEntity(req));
            await ctx.SaveChangesAsync();
            return true;
        }

        // Yarışı kaybettik: cache'i DB'nin gerçeğiyle tazele ki sonraki okumalar eski durumu
        // göstermesin. Yalnızca DB'de kalıcı olan alanlar güncellenir; routing alanları
        // (SuggestedAgentId vb.) cache'teki nesnede korunur.
        if (_byId.TryGetValue(req.Id, out var cached))
        {
            var fresh = Clone(cached);
            fresh.Status = ParseStatus(current.Status);
            fresh.AcknowledgedAt = current.AcknowledgedAt;
            fresh.ResolvedAt = current.ResolvedAt;
            fresh.AssignedTo = current.AssignedTo;
            fresh.Resolution = current.Resolution;
            _byId[req.Id] = fresh;
        }
        return false;
    }

    /// <summary>
    /// Session + ajan için açık eskalasyonu döndürür. Önce cache'e bakılır (routing alanları
    /// yalnızca orada var — DB'de saklanmıyor); yoksa DB'den okunur.
    /// </summary>
    private async Task<EscalationRequest?> FindOpenAsync(string? sessionId, string? agentName)
    {
        var cached = _byId.Values.FirstOrDefault(e =>
            (e.Status == EscalationStatus.Open || e.Status == EscalationStatus.Acknowledged)
            && string.Equals(e.SessionId, sessionId, StringComparison.Ordinal)
            && string.Equals(e.AgentName, agentName, StringComparison.Ordinal));
        if (cached is not null) return cached;

        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var row = await ctx.Escalations.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.AgentName == agentName
                     && (e.Status == "Open" || e.Status == "Acknowledged"))
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        return row is null ? null : ToDomain(row);
    }

    private static bool IsOpenDedupViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }
        && string.Equals(pg.ConstraintName, EscalationConfiguration.OpenDedupIndexName, StringComparison.Ordinal);

    /// <summary>Derin kopya — state machine paylaşılan cache nesnesini değiştirmesin diye.</summary>
    private static EscalationRequest Clone(EscalationRequest req) =>
        JsonSerializer.Deserialize<EscalationRequest>(JsonSerializer.Serialize(req))!;

    private static EscalationStatus ParseStatus(string? status) =>
        Enum.TryParse<EscalationStatus>(status, ignoreCase: true, out var s) ? s : EscalationStatus.Open;

    private static EscalationEntity ToEntity(EscalationRequest req) => new()
    {
        Id = req.Id,
        SessionId = req.SessionId,
        TraceId = req.TraceId,
        AgentName = req.AgentName,
        UserQuery = req.UserQuery,
        Reason = req.Reason,
        MissingContextJson = System.Text.Json.JsonSerializer.Serialize(req.MissingContext),
        ResponseSummary = req.ResponseSummary,
        CreatedAt = req.CreatedAt,
        AcknowledgedAt = req.AcknowledgedAt,
        ResolvedAt = req.ResolvedAt,
        Status = req.Status.ToString(),
        AssignedTo = req.AssignedTo,
        Resolution = req.Resolution
    };

    private static EscalationRequest ToDomain(EscalationEntity e)
    {
        var missingContext = string.IsNullOrWhiteSpace(e.MissingContextJson)
            ? new List<string>()
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.MissingContextJson)
              ?? new List<string>();

        var status = ParseStatus(e.Status);

        return new EscalationRequest
        {
            Id = e.Id,
            SessionId = e.SessionId,
            TraceId = e.TraceId,
            AgentName = e.AgentName,
            UserQuery = e.UserQuery,
            Reason = e.Reason,
            MissingContext = missingContext,
            ResponseSummary = e.ResponseSummary,
            CreatedAt = e.CreatedAt,
            AcknowledgedAt = e.AcknowledgedAt,
            ResolvedAt = e.ResolvedAt,
            Status = status,
            AssignedTo = e.AssignedTo,
            Resolution = e.Resolution
        };
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
            _logger.LogError(ex, "[HITL] Escalation cache hydrate başarısız.");
        }
        finally
        {
            _hydrationGate.Release();
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        // Açık olanlar her zaman + son N kapalı
        var open = await ctx.Escalations.AsNoTracking()
            .Where(e => e.Status == "Open" || e.Status == "Acknowledged")
            .ToListAsync();

        var recent = await ctx.Escalations.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(HydrateRecentCount)
            .ToListAsync();

        foreach (var e in open.Concat(recent))
        {
            _byId[e.Id] = ToDomain(e);
        }

        _logger.LogInformation(
            "[HITL] Escalation cache hydrate: open={Open}, total={Total}",
            open.Count, _byId.Count);
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void OnRemoteCreated(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var req = JsonSerializer.Deserialize<EscalationRequest>(
                root.GetProperty("payload").GetRawText());
            if (req is null) return;

            _byId.TryAdd(req.Id, req);

            try { RequestCreated?.Invoke(this, req); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteEscalationCreated parse hatası");
        }
    }

    private void OnRemoteDecided(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var req = JsonSerializer.Deserialize<EscalationRequest>(
                root.GetProperty("payload").GetRawText());
            if (req is null) return;

            _byId[req.Id] = req;

            try { RequestDecided?.Invoke(this, req); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteEscalationDecided parse hatası");
        }
    }

    private void PublishRedis(string channel, EscalationRequest req)
    {
        var payload = new { nodeId = _messageBus.NodeId, payload = req };
        _messageBus.Publish(channel, JsonSerializer.Serialize(payload));
    }
}

