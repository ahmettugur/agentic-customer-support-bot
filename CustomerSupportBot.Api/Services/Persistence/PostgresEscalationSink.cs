// Services/Persistence/PostgresEscalationSink.cs
// HITL — Hibrit cache + PostgreSQL eskalasyon kuyruğu + Redis pub/sub.
//
// Davranış:
//   - In-memory dictionary + RequestCreated/RequestDecided event'leri korunur.
//   - Create: DB'ye INSERT + cache'e ekle + event fire.
//   - Decide: state machine (EscalationStateFactory) cache üzerinde çalışır;
//     başarılıysa DB UPDATE + event fire.
//   - Cache lazy hydrate: son N kayıt yüklenir.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Create/Decide sonrası Redis'e yayın yapılır.
//   - Uzak pod'lar cache'i günceller ve lokal event'leri tetikler.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Hitl;
using CustomerSupportBot.Api.Models;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed class PostgresEscalationSink : IEscalationSink
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresEscalationSink> _logger;
    private readonly ISubscriber _sub;
    private readonly string _nodeId = RedisNodeId.Value;
    private readonly ConcurrentDictionary<string, EscalationRequest> _byId = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private const int HydrateRecentCount = 500;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    public PostgresEscalationSink(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IConnectionMultiplexer redis,
        ILogger<PostgresEscalationSink> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _sub = redis.GetSubscriber();
        _sub.Subscribe(RedisChannel.Literal("csbot:escalation:created"), OnRemoteCreated);
        _sub.Subscribe(RedisChannel.Literal("csbot:escalation:decided"), OnRemoteDecided);
    }

    public EscalationRequest Create(EscalationRequest request)
    {
        EnsureHydrated();

        try { InsertAsync(request).GetAwaiter().GetResult(); }
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

    public bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)
    {
        EnsureHydrated();
        if (!_byId.TryGetValue(id, out var req)) return false;

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

        try { UpdateAsync(req).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Escalation UPDATE başarısız. Id={Id}", id);
            throw;
        }

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

    private async Task UpdateAsync(EscalationRequest req)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.Escalations.FirstOrDefaultAsync(e => e.Id == req.Id);
        if (existing is null)
        {
            // Cache'de var, DB'de yok — beklenmez ama dirence INSERT yapalım.
            ctx.Escalations.Add(ToEntity(req));
        }
        else
        {
            existing.Status = req.Status.ToString();
            existing.AcknowledgedAt = req.AcknowledgedAt;
            existing.ResolvedAt = req.ResolvedAt;
            existing.AssignedTo = req.AssignedTo;
            existing.Resolution = req.Resolution;
        }
        await ctx.SaveChangesAsync();
    }

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

        var status = Enum.TryParse<EscalationStatus>(e.Status, ignoreCase: true, out var s)
            ? s : EscalationStatus.Open;

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

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Escalation cache hydrate başarısız.");
            }
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

    private void OnRemoteCreated(RedisChannel _, RedisValue val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val.ToString());
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _nodeId) return;

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

    private void OnRemoteDecided(RedisChannel _, RedisValue val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val.ToString());
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _nodeId) return;

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
        try
        {
            var payload = new { nodeId = _nodeId, payload = req };
            _sub.Publish(RedisChannel.Literal(channel), JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis escalation publish başarısız: {Channel}", channel);
        }
    }
}
