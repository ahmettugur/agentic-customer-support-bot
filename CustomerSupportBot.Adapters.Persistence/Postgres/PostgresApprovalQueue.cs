// Services/Persistence/PostgresApprovalQueue.cs
// HITL — Hibrit cache + PostgreSQL approval queue.
//
// Önemli: TaskCompletionSource süreç-içi senkronizasyon primitifidir;
// PERSIST EDİLMEZ. Restart edilirse bekleyen TCS'ler kaybolur — DB'deki
// Pending kayıtlar PersistenceHydrator tarafından startup'ta Expired'a
// çevrilir (audit trail).
//
// Davranış (in-memory ile aynı):
//   - Create: DB'ye INSERT (Pending) + cache + TCS + RequestCreated event.
//   - AwaitDecisionAsync: TCS task'ını bekler. Timeout'ta otomatik karar.
//   - Decide: cache + TCS release + DB UPDATE + RequestDecided event.
//   - Lazy hydrate: tüm açık (Pending) + son N karar yüklenir.
//     (Hydrate edilen Pending'ler için yeni TCS oluşturulmaz çünkü orijinal
//      tool lambda'sı zaten ölmüş; hydrator startup'ta bunları Expired'a çeker.
//      Burada cache'e sadece "sahipsiz" kayıt olarak okuma için ekleriz.)
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Create: csbot:approval:created kanalına yayın → diğer pod'lar entry'yi cache'e ekler.
//   - Decide: csbot:approval:decided kanalına yayın → TCS o pod'da hangi pod'da bulunursa
//     orada tetiklenir.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresApprovalQueue : IApprovalQueue
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ApprovalOptions _options;
    private readonly ILogger<PostgresApprovalQueue> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly IAppDistributedLock _distributedLock;
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private const int HydrateRecentCount = 200;

    public event EventHandler<ApprovalRequest>? RequestCreated;
    public event EventHandler<ApprovalRequest>? RequestDecided;

    public PostgresApprovalQueue(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IOptions<ApprovalOptions> options,
        IMessageBusPort messageBus,
        IAppDistributedLock distributedLock,
        ILogger<PostgresApprovalQueue> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _distributedLock = distributedLock;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe("csbot:approval:created", OnRemoteCreated);
        _messageBus.Subscribe("csbot:approval:decided", OnRemoteDecided);
    }

    public async Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        EnsureHydrated();

        request.TimeoutSeconds = _options.TimeoutSeconds;
        var tcs = new TaskCompletionSource<ApprovalRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new QueueEntry(request, tcs);
        _entries[request.Id] = entry;

        try { await InsertAsync(request, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Approval INSERT başarısız. Id={Id}", request.Id);
            _entries.TryRemove(request.Id, out _);
            throw ExceptionTranslator.Translate(ex, $"Approval oluşturulamadı: {request.Id}");
        }

        _logger.LogInformation(
            "[HITL] Approval request created: id={Id}, tool={Tool}, session={Session}",
            request.Id, request.ToolName, request.SessionId);

        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

        PublishRedis("csbot:approval:created", new
        {
            nodeId = _messageBus.NodeId,
            id = request.Id,
            sessionId = request.SessionId,
            traceId = request.TraceId,
            toolName = request.ToolName,
            agentName = request.AgentName,
            parametersJson = JsonSerializer.Serialize(request.Parameters),
            userQuery = request.UserQuery,
            justification = request.Justification,
            requestedAt = request.RequestedAt,
            timeoutSeconds = request.TimeoutSeconds
        });

        return request;
    }

    public async Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"Approval request not found: {id}");

        // Hydrate edilmiş "orphan" kayıt — TCS gerçek bir bekleyene bağlı değil.
        if (entry.Tcs is null)
            throw new InvalidOperationException($"Approval request is orphan (no live waiter): {id}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(entry.Request.TimeoutSeconds));

        // CancellationToken.Register yalnızca senkron Action kabul eder; asenkron
        // Decide/Update çağrılarını burada thread'i bloklamadan (fire-and-forget,
        // Task.Run ile) tetikliyoruz. entry.Tcs.Task zaten bu işin bitmesini bekler.
        using (cts.Token.Register(() => _ = HandleTimeoutAsync(id, entry)))
        {
            return await entry.Tcs.Task.ConfigureAwait(false);
        }
    }

    private async Task HandleTimeoutAsync(string id, QueueEntry entry)
    {
        if (entry.Tcs is null || entry.Tcs.Task.IsCompleted) return;

        var autoApprove = _options.AutoApproveOnTimeout;
        try
        {
            await DecideAsync(
                id,
                approved: autoApprove,
                decidedBy: WellKnown.Defaults.System,
                reason: autoApprove
                    ? WellKnown.ApprovalReasons.AutoApproveTimeout
                    : WellKnown.ApprovalReasons.TimeoutExpired).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Timeout auto-decide başarısız. Id={Id}", id);
        }

        if (!autoApprove)
        {
            entry.Request.Status = ApprovalStatus.Expired;
            try { await UpdateAsync(entry.Request).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval Expired UPDATE başarısız. Id={Id}", id);
            }
        }
    }

    public async Task<bool> DecideAsync(
        string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)
    {
        EnsureHydrated();
        if (!_entries.TryGetValue(id, out var entry)) return false;

        // Distributed lock: farklı pod'lardan eş zamanlı DecideAsync() çağrılarını serialize eder.
        // TryAcquireAsync null dönerse (başka pod lock tutuyor) kararı reddet — double-decision önlemi.
        var handle = await _distributedLock.TryAcquireAsync($"approval:{id}", ct: ct).ConfigureAwait(false);
        if (handle is null)
        {
            _logger.LogWarning("[HITL] Approval distributed lock alınamadı; karar reddedildi. Id={Id}", id);
            return false;
        }

        try
        {
            // Lock altında güncel durumu kontrol et
            if (entry.Request.Status != ApprovalStatus.Pending) return false;

            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.DecidedAt = DateTime.UtcNow;
            entry.Request.DecidedBy = string.IsNullOrWhiteSpace(decidedBy) ? WellKnown.Defaults.Admin : decidedBy;
            entry.Request.DecisionReason = reason;

            try { await UpdateAsync(entry.Request).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval UPDATE başarısız. Id={Id}", id);
                throw;
            }

            _logger.LogInformation(
                "[HITL] Approval decision: id={Id}, approved={Approved}, by={By}",
                id, approved, entry.Request.DecidedBy);

            try { RequestDecided?.Invoke(this, entry.Request); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

            entry.Tcs?.TrySetResult(entry.Request);

            PublishRedis("csbot:approval:decided", new
            {
                nodeId = _messageBus.NodeId,
                id,
                approved,
                decidedBy = entry.Request.DecidedBy,
                reason = entry.Request.DecisionReason,
                decidedAt = entry.Request.DecidedAt
            });

            return true;
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ApprovalRequest> GetPending()
    {
        EnsureHydrated();
        return _entries.Values
            .Where(e => e.Request.Status == ApprovalStatus.Pending)
            .Select(e => e.Request)
            .OrderBy(r => r.RequestedAt)
            .ToList();
    }

    public IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)
    {
        EnsureHydrated();
        return _entries.Values
            .Select(e => e.Request)
            .OrderByDescending(r => r.RequestedAt)
            .Take(count)
            .ToList();
    }

    public ApprovalRequest? Get(string id)
    {
        EnsureHydrated();
        return _entries.TryGetValue(id, out var entry) ? entry.Request : null;
    }

    /// <summary>
    /// PersistenceHydrator startup'ta çağırır — Pending kayıtları toplu olarak Expired'a çeker.
    /// </summary>
    public async Task ExpirePendingOnStartupAsync(TimeSpan minAge, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - minAge;

        var stale = await ctx.Approvals
            .Where(a => a.Status == "Pending" && a.RequestedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var e in stale)
        {
            e.Status = "Expired";
            e.DecidedAt = now;
            e.DecidedBy = WellKnown.Defaults.System;
            e.DecisionReason = WellKnown.ApprovalReasons.TimeoutExpired;
        }

        await ctx.SaveChangesAsync(ct);
        _logger.LogWarning(
            "[HITL] {Count} stale Pending approval Expired'a çevrildi (restart recovery).",
            stale.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task InsertAsync(ApprovalRequest req, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        ctx.Approvals.Add(ToEntity(req));
        await ctx.SaveChangesAsync(ct);
    }

    private async Task UpdateAsync(ApprovalRequest req)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.Approvals.FirstOrDefaultAsync(a => a.Id == req.Id);
        if (existing is null)
        {
            ctx.Approvals.Add(ToEntity(req));
        }
        else
        {
            existing.Status = req.Status.ToString();
            existing.DecidedAt = req.DecidedAt;
            existing.DecidedBy = req.DecidedBy;
            existing.DecisionReason = req.DecisionReason;
        }
        await ctx.SaveChangesAsync();
    }

    private static ApprovalRequestEntity ToEntity(ApprovalRequest req) => new()
    {
        Id = req.Id,
        SessionId = req.SessionId,
        TraceId = req.TraceId,
        ToolName = req.ToolName,
        AgentName = req.AgentName,
        ParametersJson = System.Text.Json.JsonSerializer.Serialize(req.Parameters),
        UserQuery = req.UserQuery,
        Justification = req.Justification,
        RequestedAt = req.RequestedAt,
        DecidedAt = req.DecidedAt,
        Status = req.Status.ToString(),
        DecidedBy = req.DecidedBy,
        DecisionReason = req.DecisionReason,
        TimeoutSeconds = req.TimeoutSeconds
    };

    private static ApprovalRequest ToDomain(ApprovalRequestEntity e)
    {
        var parameters = string.IsNullOrWhiteSpace(e.ParametersJson)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(e.ParametersJson)
              ?? new Dictionary<string, object?>();

        var status = Enum.TryParse<ApprovalStatus>(e.Status, ignoreCase: true, out var s)
            ? s : ApprovalStatus.Pending;

        return new ApprovalRequest
        {
            Id = e.Id,
            SessionId = e.SessionId,
            TraceId = e.TraceId,
            ToolName = e.ToolName,
            AgentName = e.AgentName,
            Parameters = parameters,
            UserQuery = e.UserQuery,
            Justification = e.Justification,
            RequestedAt = e.RequestedAt,
            DecidedAt = e.DecidedAt,
            Status = status,
            DecidedBy = e.DecidedBy,
            DecisionReason = e.DecisionReason,
            TimeoutSeconds = e.TimeoutSeconds
        };
    }

    // Kasıtlı olarak sync-blocking: GetPending/GetRecent/Get senkron port metotları
    // (admin panel salt-okunur uçları) olduğu için burada async'e geçmek onları da
    // async'e zorlar. Bloklama süreç ömrü boyunca yalnızca BİR KEZ (ilk çağrıda)
    // gerçekleşir — CreateAsync/DecideAsync'teki her-istekte-bir DB round-trip'i ile
    // aynı sınıfta değildir, bu yüzden kapsam dışı bırakıldı.
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
                _logger.LogError(ex, "[HITL] Approval cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var rows = await ctx.Approvals.AsNoTracking()
            .OrderByDescending(a => a.RequestedAt)
            .Take(HydrateRecentCount)
            .ToListAsync();

        foreach (var e in rows)
        {
            // Orphan: TCS yok → AwaitDecisionAsync çağrılırsa hata atar (zaten karar verilmiş kayıtlar).
            _entries[e.Id] = new QueueEntry(ToDomain(e), tcs: null);
        }

        _logger.LogInformation("[HITL] Approval cache hydrate: {Count} kayıt", rows.Count);
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void OnRemoteCreated(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString()!;
            if (_entries.ContainsKey(id)) return;

            var parametersJson = root.GetProperty("parametersJson").GetString() ?? "{}";
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(parametersJson)
                             ?? new Dictionary<string, object?>();

            var req = new ApprovalRequest
            {
                Id = id,
                SessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null,
                TraceId = root.TryGetProperty("traceId", out var tr) ? tr.GetString() : null,
                ToolName = root.GetProperty("toolName").GetString() ?? "",
                AgentName = root.TryGetProperty("agentName", out var an) ? an.GetString() : null,
                Parameters = parameters,
                UserQuery = root.TryGetProperty("userQuery", out var uq) ? uq.GetString() : null,
                Justification = root.TryGetProperty("justification", out var j) ? j.GetString() : null,
                RequestedAt = root.GetProperty("requestedAt").GetDateTime(),
                TimeoutSeconds = root.GetProperty("timeoutSeconds").GetInt32(),
                Status = ApprovalStatus.Pending
            };

            _entries.TryAdd(req.Id, new QueueEntry(req, tcs: null));

            try { RequestCreated?.Invoke(this, req); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteCreated parse hatası");
        }
    }

    private void OnRemoteDecided(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString()!;
            if (!_entries.TryGetValue(id, out var entry)) return;
            if (entry.Tcs is null || entry.Tcs.Task.IsCompleted) return;

            var approved = root.GetProperty("approved").GetBoolean();
            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.DecidedAt = root.TryGetProperty("decidedAt", out var da) && da.ValueKind != JsonValueKind.Null
                ? da.GetDateTime() : DateTime.UtcNow;
            entry.Request.DecidedBy = root.TryGetProperty("decidedBy", out var db) ? db.GetString() : null;
            entry.Request.DecisionReason = root.TryGetProperty("reason", out var r) && r.ValueKind != JsonValueKind.Null
                ? r.GetString() : null;

            entry.Tcs.TrySetResult(entry.Request);

            try { RequestDecided?.Invoke(this, entry.Request); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteDecided parse hatası");
        }
    }

    private void PublishRedis(string channel, object payload)
    {
        _messageBus.Publish(channel, JsonSerializer.Serialize(payload));
    }

    // ─────────────────────────────────────────────────────────────────────────

    private sealed class QueueEntry
    {
        public ApprovalRequest Request { get; }
        public TaskCompletionSource<ApprovalRequest>? Tcs { get; }

        public QueueEntry(ApprovalRequest req, TaskCompletionSource<ApprovalRequest>? tcs)
        {
            Request = req;
            Tcs = tcs;
        }
    }
}

