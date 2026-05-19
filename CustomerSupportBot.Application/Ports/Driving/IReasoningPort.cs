// Ports/Driving/IReasoningPort.cs
// PRIMARY PORT — Reasoning kullanım senaryosu.
// ChatStreamOrchestrator bu port'u çağırarak ön-analiz üretir.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Kullanıcı mesajını ön-analiz eden reasoning port'u.
/// </summary>
public interface IReasoningPort
{
    /// <summary>Non-streaming reasoning — tek ReasoningResult döndürür.</summary>
    Task<ReasoningResult> ReasonAsync(
        string query,
        AgentSession session,
        List<ChatMessage>? history = null,
        CancellationToken ct = default);

    /// <summary>Streaming reasoning — delta event'leri yayar.</summary>
    IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
        string query,
        AgentSession session,
        List<ChatMessage>? history = null,
        CancellationToken ct = default);
}
