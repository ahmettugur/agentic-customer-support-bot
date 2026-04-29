// Services/InMemoryApprovalQueue.cs
// HITL — Thread-safe in-memory approval queue. Her request için bir
// TaskCompletionSource tutulur; Decide çağrılınca bu TCS tetiklenir ve
// AwaitDecisionAsync release olur.
//
// Production için: bunun yerine Redis + pub/sub (çoklu instance) veya
// Database-backed bir impl yazılmalı. Şu an için in-memory yeterli.

using System.Collections.Concurrent;
using CustomerSupportBot.Models;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Services;

public class InMemoryApprovalQueue : IApprovalQueue
{
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();
    private readonly ConcurrentQueue<string> _order = new(); // FIFO + history
    private readonly ApprovalOptions _options;
    private readonly ILogger<InMemoryApprovalQueue> _logger;
    private const int HistoryCapacity = 200;

    public event EventHandler<ApprovalRequest>? RequestCreated;
    public event EventHandler<ApprovalRequest>? RequestDecided;

    public InMemoryApprovalQueue(
        IOptions<ApprovalOptions> options,
        ILogger<InMemoryApprovalQueue> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ApprovalRequest Create(ApprovalRequest request)
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

        return request;
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
            // Timeout → auto decision
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
                // Expired özelliğini güncelle
                entry.Request.Status = ApprovalStatus.Expired;
            }
        }))
        {
            return await entry.Tcs.Task.ConfigureAwait(false);
        }
    }

    public bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null)
    {
        if (!_entries.TryGetValue(id, out var entry)) return false;
        lock (entry.Lock)
        {
            if (entry.Request.Status != ApprovalStatus.Pending) return false;

            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.DecidedAt = DateTime.UtcNow;
            entry.Request.DecidedBy = string.IsNullOrWhiteSpace(decidedBy) ? WellKnown.Defaults.Admin : decidedBy;
            entry.Request.DecisionReason = reason;

            _logger.LogInformation(
                "[HITL] Approval decision: id={Id}, approved={Approved}, by={By}",
                id, approved, entry.Request.DecidedBy);

            try { RequestDecided?.Invoke(this, entry.Request); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

            entry.Tcs.TrySetResult(entry.Request);
            return true;
        }
    }

    public IReadOnlyList<ApprovalRequest> GetPending() =>
        _entries.Values
            .Where(e => e.Request.Status == ApprovalStatus.Pending)
            .Select(e => e.Request)
            .OrderBy(r => r.RequestedAt)
            .ToList();

    public IReadOnlyList<ApprovalRequest> GetRecent(int count = 50) =>
        _entries.Values
            .Select(e => e.Request)
            .OrderByDescending(r => r.RequestedAt)
            .Take(count)
            .ToList();

    public ApprovalRequest? Get(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry.Request : null;

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
