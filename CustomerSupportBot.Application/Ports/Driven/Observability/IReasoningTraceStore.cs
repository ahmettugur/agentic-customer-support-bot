// Ports/Driven/Observability/IReasoningTraceStore.cs
// SECONDARY PORT — Reasoning trace kalıcılığı ve okunması.
// Adaptörler: PostgresReasoningTraceStore, InMemoryReasoningTraceStore

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Observability;

/// <summary>
/// Agent reasoning trace'lerinin gözlemlenebilirlik kaydı için secondary port.
/// </summary>
public interface IReasoningTraceStore
{
    ReasoningTrace StartTrace(string sessionId, string query);
    void Update(ReasoningTrace trace);
    void Complete(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null);

    IReadOnlyList<ReasoningTrace> GetRecent(int count = 50);
    IReadOnlyList<ReasoningTrace> GetBySession(string sessionId);
    ReasoningTrace? Get(string traceId);
}
