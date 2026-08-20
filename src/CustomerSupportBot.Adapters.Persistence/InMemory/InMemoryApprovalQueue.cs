// Services/InMemoryApprovalQueue.cs
// HITL — Thread-safe in-memory approval queue. Her request için bir
// TaskCompletionSource tutulur; Decide çağrılınca bu TCS tetiklenir ve
// AwaitDecisionAsync release olur.
//
// Production için: bunun yerine Redis + pub/sub (çoklu instance) veya
// Database-backed bir impl yazılmalı. Şu an için in-memory yeterli.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public class InMemoryApprovalQueue : IApprovalQueue
{
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();
    private readonly ConcurrentQueue<string> _order = new(); // FIFO + history
    private readonly ApprovalOptions _options;
    private readonly IApprovalExecutionRouter _executionRouter;
    private readonly ILogger<InMemoryApprovalQueue> _logger;
    private const int HistoryCapacity = 200;

    public event EventHandler<ApprovalRequest>? RequestCreated;
    public event EventHandler<ApprovalRequest>? RequestDecided;

    public InMemoryApprovalQueue(
        IOptions<ApprovalOptions> options,
        IApprovalExecutionRouter executionRouter,
        ILogger<InMemoryApprovalQueue> logger)
    {
        _options = options.Value;
        _executionRouter = executionRouter;
        _logger = logger;
    }

    public Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        request.TimeoutSeconds = _options.TimeoutSeconds;
        var tcs = new TaskCompletionSource<ApprovalRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new QueueEntry(request, tcs);
        _entries[request.Id] = entry;
        _order.Enqueue(request.Id);
        TrimHistory();

        _logger.LogInformation(
            "[HITL] Approval request created: id={Id}, tool={Tool}, session={Session}",
            request.Id, request.ToolName, request.SessionId);

        // Event subscribers (SSE layer) bilgilendir
        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

        return Task.FromResult(request);
    }

    public async Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"Approval request not found: {id}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(entry.Request.TimeoutSeconds));

        using (cts.Token.Register(() =>
        {
            if (entry.Tcs.Task.IsCompleted) return;
            // Timeout → auto decision. Bu implementasyon tamamen bellek-içi (I/O yok),
            // bu yüzden DecideAsync senkron tamamlanır — Task.Run'a gerek yok.
            var autoApprove = _options.AutoApproveOnTimeout;
            _ = DecideAsync(
                id,
                approved: autoApprove,
                decidedBy: WellKnown.Defaults.System,
                reason: autoApprove
                    ? WellKnown.ApprovalReasons.AutoApproveTimeout
                    : WellKnown.ApprovalReasons.TimeoutExpired);
            if (!autoApprove)
            {
                // Expired özelliğini güncelle
                entry.Request.Status = ApprovalStatus.Expired;
            }
        }))
        {
            return await entry.Tcs.Task.ConfigureAwait(false);
        }
    }

    public async Task<bool> DecideAsync(
        string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry)) return false;

        // Status'ü Pending'den çıkarmak eşzamanlı ikinci bir DecideAsync çağrısını
        // (double-decision) burada, senkron olarak engeller — asenkron yürütme
        // ADIMI kilidin DIŞINDA olsa da, bu kontrol yeterli çünkü bir kez Approved/Rejected'a
        // geçtikten sonra hiçbir çağrı ikinci kez buraya giremez.
        lock (entry.Lock)
        {
            if (entry.Request.Status != ApprovalStatus.Pending) return false;

            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.ExecutionStatus = approved
                ? ApprovalExecutionStatus.Running
                : ApprovalExecutionStatus.None;
            entry.Request.DecidedAt = DateTime.UtcNow;
            entry.Request.DecidedBy = string.IsNullOrWhiteSpace(decidedBy) ? WellKnown.Defaults.Admin : decidedBy;
            entry.Request.DecisionReason = reason;
        }

        if (approved)
        {
            try
            {
                var outcome = await _executionRouter.ExecuteAsync(entry.Request, ct).ConfigureAwait(false);
                entry.Request.ExecutionResult = outcome.Message;
                entry.Request.ExecutionStatus = outcome.Success
                    ? ApprovalExecutionStatus.Succeeded
                    : ApprovalExecutionStatus.Failed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval execution başarısız. Id={Id}", id);
                entry.Request.ExecutionResult = "İşlem yürütülürken bir hata oluştu.";
                entry.Request.ExecutionStatus = ApprovalExecutionStatus.Failed;
            }
            entry.Request.ExecutedAt = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "[HITL] Approval decision: id={Id}, approved={Approved}, by={By}",
            id, approved, entry.Request.DecidedBy);

        try { RequestDecided?.Invoke(this, entry.Request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

        entry.Tcs.TrySetResult(entry.Request);
        return true;
    }

    public IReadOnlyList<ApprovalRequest> GetPending() =>
        _entries.Values
            .Where(e => e.Request.Status == ApprovalStatus.Pending)
            .Select(e => e.Request)
            .OrderBy(r => r.RequestedAt)
            .ToList();

    /// <summary>
    /// Bu adaptörde cache ile kalıcı kaynak AYNI şeydir (süreç-içi sözlük), dolayısıyla
    /// senkron sürümle aynı listeyi döner. Postgres adaptöründeki ayrım — cache'in Redis
    /// mesajı kaybı yüzünden eksik kalabilmesi — burada var olamaz.
    /// </summary>
    public Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default) =>
        Task.FromResult(GetPending());

    public IReadOnlyList<ApprovalRequest> GetRecent(int count = 50) =>
        _entries.Values
            .Select(e => e.Request)
            .OrderByDescending(r => r.RequestedAt)
            .Take(count)
            .ToList();

    public ApprovalRequest? Get(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry.Request : null;

    // Tek süreç: cache zaten kayıtların tek kaynağıdır, ayrı bir kalıcı depo yoktur.
    public Task<IReadOnlyList<ApprovalRequest>> GetUnseenForSessionAsync(
        string sessionId, string customerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>(_entries.Values
            .Select(e => e.Request)
            .Where(r =>
                string.Equals(r.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(r.CustomerId, customerId, StringComparison.Ordinal)
                && r.Status != ApprovalStatus.Pending
                && r.CustomerSeenAt is null)
            .OrderBy(r => r.DecidedAt)
            .ToList());

    public Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>(_entries.Values
            .Select(e => e.Request)
            .Where(r => r.Status == ApprovalStatus.Approved
                     && r.ExecutionStatus == ApprovalExecutionStatus.Running
                     && r.DecidedAt is not null
                     && r.DecidedAt < DateTime.UtcNow - TimeSpan.FromMinutes(
                            Math.Max(1, _options.StuckExecutionAfterMinutes)))
            .OrderBy(r => r.DecidedAt)
            .ToList());

    // Tek süreç: cache zaten kayıtların tek kaynağıdır.
    public Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Get(id));

    public Task<bool> MarkSeenAsync(string id, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(id, out var entry))
            entry.Request.CustomerSeenAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ApprovalRequest>> GetHistoryForCustomerAsync(
        string customerId, int count = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>(_entries.Values
            .Select(e => e.Request)
            .Where(r => string.Equals(r.CustomerId, customerId, StringComparison.Ordinal))
            .OrderByDescending(r => r.RequestedAt)
            .Take(count)
            .ToList());

    private void TrimHistory()
    {
        // Ring buffer: en eski karar verilmiş request'leri at
        while (_order.Count > HistoryCapacity)
        {
            if (!_order.TryDequeue(out var oldId)) break;
            if (_entries.TryGetValue(oldId, out var e) &&
                e.Request.Status != ApprovalStatus.Pending)
            {
                _entries.TryRemove(oldId, out _);
            }
            else
            {
                // Pending ise geri koy
                _order.Enqueue(oldId);
                break;
            }
        }
    }

    private sealed class QueueEntry
    {
        public ApprovalRequest Request { get; }
        public TaskCompletionSource<ApprovalRequest> Tcs { get; }
        public object Lock { get; } = new();

        public QueueEntry(ApprovalRequest req, TaskCompletionSource<ApprovalRequest> tcs)
        {
            Request = req;
            Tcs = tcs;
        }
    }
}

