// Application/Services/TracePortService.cs
// DRIVING PORT IMPL — ITracePort → IReasoningTraceStore + ISessionManager.

using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Telemetry;

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

    public async Task<IReadOnlyList<TracedSessionSummary>> GetSessionsSummaryAsync(CancellationToken ct = default)
    {
        var allTraces = _traces.GetRecent(500);
        var result = new List<TracedSessionSummary>();

        foreach (var g in allTraces.GroupBy(t => t.SessionId))
        {
            var traces = g.OrderByDescending(t => t.StartedAt).ToList();
            var firstQuery = traces.LastOrDefault()?.UserQuery;
            var title = firstQuery != null
                ? (firstQuery.Length > 60 ? firstQuery[..60] + "…" : firstQuery)
                : "Yeni Oturum";
            var history = await _sessions.GetHistoryAsync(g.Key, ct);
            result.Add(new TracedSessionSummary(
                g.Key,
                title,
                traces.Count,
                traces.First().StartedAt,
                traces.First().UserQuery,
                history.Count));
        }

        return result.OrderByDescending(s => s.LastTraceAt).ToList();
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
