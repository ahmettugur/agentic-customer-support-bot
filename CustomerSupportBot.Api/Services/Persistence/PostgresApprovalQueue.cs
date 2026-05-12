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

using System.Collections.Concurrent;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Hitl;
using CustomerSupportBot.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed class PostgresApprovalQueue : IApprovalQueue
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ApprovalOptions _options;
    private readonly ILogger<PostgresApprovalQueue> _logger;
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private const int HydrateRecentCount = 200;

    public event EventHandler<ApprovalRequest>? RequestCreated;
    public event EventHandler<ApprovalRequest>? RequestDecided;

    public PostgresApprovalQueue(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IOptions<ApprovalOptions> options,
        ILogger<PostgresApprovalQueue> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;
    }

    public ApprovalRequest Create(ApprovalRequest request)
    {
        EnsureHydrated();

        request.TimeoutSeconds = _options.TimeoutSeconds;
        var tcs = new TaskCompletionSource<ApprovalRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new QueueEntry(request, tcs);
        _entries[request.Id] = entry;

        try { InsertAsync(request).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Approval INSERT başarısız. Id={Id}", request.Id);
            _entries.TryRemove(request.Id, out _);
            throw;
        }

        _logger.LogInformation(
            "[HITL] Approval request created: id={Id}, tool={Tool}, session={Session}",
            request.Id, request.ToolName, request.SessionId);

        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

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

        using (cts.Token.Register(() =>
        {
            if (entry.Tcs.Task.IsCompleted) return;
            var autoApprove = _options.AutoApproveOnTimeout;
            Decide(
                id,
                approved: autoApprove,
                decidedBy: WellKnown.Defaults.System,
                reason: autoApprove
                    ? WellKnown.ApprovalReasons.AutoApproveTimeout
                    : WellKnown.ApprovalReasons.TimeoutExpired);
            if (!autoApprove)
            {
                entry.Request.Status = ApprovalStatus.Expired;
                // Status değişti — DB güncellemesi de gerek
                try { UpdateAsync(entry.Request).GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HITL] Approval Expired UPDATE başarısız. Id={Id}", id);
                }
            }
        }))
        {
            return await entry.Tcs.Task.ConfigureAwait(false);
        }
    }

    public bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null)
    {
        EnsureHydrated();
        if (!_entries.TryGetValue(id, out var entry)) return false;

        lock (entry.Lock)
        {
            if (entry.Request.Status != ApprovalStatus.Pending) return false;

            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.DecidedAt = DateTime.UtcNow;
            entry.Request.DecidedBy = string.IsNullOrWhiteSpace(decidedBy) ? WellKnown.Defaults.Admin : decidedBy;
            entry.Request.DecisionReason = reason;

            try { UpdateAsync(entry.Request).GetAwaiter().GetResult(); }
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
            return true;
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
    internal async Task ExpirePendingOnStartupAsync(TimeSpan minAge, CancellationToken ct = default)
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

    private async Task InsertAsync(ApprovalRequest req)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.Approvals.Add(ToEntity(req));
        await ctx.SaveChangesAsync();
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

    private sealed class QueueEntry
    {
        public ApprovalRequest Request { get; }
        public TaskCompletionSource<ApprovalRequest>? Tcs { get; }
        public object Lock { get; } = new();

        public QueueEntry(ApprovalRequest req, TaskCompletionSource<ApprovalRequest>? tcs)
        {
            Request = req;
            Tcs = tcs;
        }
    }
}
