// Application/Services/TracePortService.cs
// DRIVING PORT IMPL — ITracePort → IReasoningTraceStore + ISessionManager.

using CustomerSupportBot.Application.Ports.Driven.Observability;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

public sealed class TracePortService : ITracePort
{
    private readonly IReasoningTraceStore _traces;
    private readonly ISessionManager _sessions;

    public TracePortService(IReasoningTraceStore traces, ISessionManager sessions)
    {
        _traces = traces;
        _sessions = sessions;
    }

    public IReadOnlyList<ReasoningTrace> GetRecentTraces(int count = 20)
        => _traces.GetRecent(count);

    public ReasoningTrace? GetTrace(string traceId)
        => _traces.Get(traceId);

    public IReadOnlyList<ReasoningTrace> GetTracesBySession(string sessionId)
        => _traces.GetBySession(sessionId);

    public IReadOnlyList<TracedSessionSummary> GetSessionsSummary()
    {
        var allTraces = _traces.GetRecent(500);
        return allTraces
            .GroupBy(t => t.SessionId)
            .Select(g =>
            {
                var traces = g.OrderByDescending(t => t.StartedAt).ToList();
                var firstQuery = traces.LastOrDefault()?.UserQuery;
                var title = firstQuery != null
                    ? (firstQuery.Length > 60 ? firstQuery[..60] + "…" : firstQuery)
                    : "Yeni Oturum";
                var messageCount = _sessions.GetHistory(g.Key).Count;
                return new TracedSessionSummary(
                    g.Key,
                    title,
                    traces.Count,
                    traces.First().StartedAt,
                    traces.First().UserQuery,
                    messageCount);
            })
            .OrderByDescending(s => s.LastTraceAt)
            .ToList();
    }

    public TraceStatsSummary GetStats()
    {
        var all = _traces.GetRecent(500);
        if (all.Count == 0)
            return new TraceStatsSummary(0, 0, 0, 0.0, 0.0,
                new Dictionary<string, int>());

        var completed = all.Where(t => t.CompletedAt.HasValue).ToList();
        var terminationCounts = completed
            .Where(t => !string.IsNullOrEmpty(t.TerminationReason))
            .GroupBy(t => t.TerminationReason!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TraceStatsSummary(
            all.Count,
            completed.Count,
            all.Count(t => !string.IsNullOrEmpty(t.Error)),
            completed.Any()
                ? completed.Where(t => t.DurationMs.HasValue).Average(t => t.DurationMs!.Value)
                : 0.0,
            completed.Any()
                ? completed.Average(t => (double)t.IterationCount)
                : 0.0,
            terminationCounts);
    }
}
