// Ports/Driving/IAgentTeamPort.cs
// PRIMARY PORT — Ajan takımının dış sözleşmesi.
// HTTP/SSE driving adapter'ları (ChatEndpoints, AdminEndpoints, AgentPanelEndpoints)
// ve ChatStreamOrchestrator bu port'a bağımlıdır; CustomerSupportTeam implementasyonuna değil.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Müşteri destek ajan takımının primary (driving) port sözleşmesi.
/// Implementasyon (CustomerSupportTeam) Api.Agents katmanında yaşar ve
/// Microsoft.Agents framework'üne bağımlıdır; bu port o detayları gizler.
/// </summary>
public interface IAgentTeamPort
{
    /// <summary>
    /// Kullanıcı sorgusunu workflow'da koşturur ve nihai yanıtı döndürür (non-streaming).
    /// </summary>
    Task<string> RunAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null);

    /// <summary>
    /// Workflow'u SSE stream event'leri olarak koşturur.
    /// </summary>
    IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default);
}
