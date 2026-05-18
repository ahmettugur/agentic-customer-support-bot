// Services/InMemoryReasoningTraceStore.cs
// In-memory ring buffer tabanlı trace store.
// ConcurrentDictionary + capped list kullanılır; lock'suz read, hafif write.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public class InMemoryReasoningTraceStore : IReasoningTraceStore
{
    private readonly ConcurrentDictionary<string, ReasoningTrace> _byId = new();
    private readonly int _maxCapacity;
    private readonly ConcurrentQueue<string> _insertionOrder = new();

    public InMemoryReasoningTraceStore(int maxCapacity = 500)
    {
        _maxCapacity = maxCapacity;
    }

    public ReasoningTrace StartTrace(string sessionId, string userQuery)
    {
        var trace = new ReasoningTrace
        {
            SessionId = sessionId,
            UserQuery = userQuery,
            StartedAt = DateTime.UtcNow
        };

        _byId[trace.TraceId] = trace;
        _insertionOrder.Enqueue(trace.TraceId);

        // Kapasite taşınca en eski trace'i düşür
        while (_insertionOrder.Count > _maxCapacity && _insertionOrder.TryDequeue(out var oldId))
        {
            _byId.TryRemove(oldId, out _);
        }

        return trace;
    }

    public void Update(ReasoningTrace trace)
    {
        // ReasoningTrace referansı zaten store içinde — no-op safe
        _byId[trace.TraceId] = trace;
    }

    public void Complete(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null)
    {
        if (!_byId.TryGetValue(traceId, out var trace)) return;

        trace.CompletedAt = DateTime.UtcNow;
        if (terminationReason != null) trace.TerminationReason = terminationReason;
        if (finalResponse != null)
        {
            // Truncate büyük yanıtları
            trace.FinalResponse = finalResponse.Length > 2000
                ? finalResponse[..2000] + "…"
                : finalResponse;
        }
        if (error != null) trace.Error = error;
    }

    public ReasoningTrace? Get(string traceId)
    {
        _byId.TryGetValue(traceId, out var trace);
        return trace;
    }

    public IReadOnlyList<ReasoningTrace> GetRecent(int count = 50)
    {
        // Insertion order'dan ters yönde dön
        return _byId.Values
            .OrderByDescending(t => t.StartedAt)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<ReasoningTrace> GetBySession(string sessionId)
    {
        return _byId.Values
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.StartedAt)
            .ToList();
    }
}

