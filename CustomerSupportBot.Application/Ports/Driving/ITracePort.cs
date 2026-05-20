// Ports/Driving/ITracePort.cs
// PRIMARY PORT — Reasoning trace gözlemlenebilirliği.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

public sealed record TracedSessionSummary(
    string SessionId,
    string Title,
    int TraceCount,
    DateTime LastTraceAt,
    string? LastQuery,
    int MessageCount);

public sealed record TraceStatsSummary(
    int TotalTraces,
    int CompletedCount,
    int ErrorCount,
    double AvgDurationMs,
    double AvgIterationCount,
    IReadOnlyDictionary<string, int> TerminationReasons);

/// <summary>
/// Reasoning trace okuma ve istatistik için primary (driving) port.
/// </summary>
public interface ITracePort
{
    IReadOnlyList<ReasoningTrace> GetRecentTraces(int count = 20);
    ReasoningTrace? GetTrace(string traceId);
    IReadOnlyList<ReasoningTrace> GetTracesBySession(string sessionId);
    IReadOnlyList<TracedSessionSummary> GetSessionsSummary();
    TraceStatsSummary GetStats();
}
