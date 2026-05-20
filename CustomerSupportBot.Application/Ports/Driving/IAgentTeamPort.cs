// Ports/Driving/IAgentTeamPort.cs
// PRIMARY PORT — Ajan takımının dış sözleşmesi.
// HTTP/SSE driving adapter'ları (ChatEndpoints, AdminEndpoints, AgentPanelEndpoints)
// ve ChatPortService bu port'a bağımlıdır; CustomerSupportTeam implementasyonuna değil.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Müşteri destek ajan takımının primary (driving) port sözleşmesi.
/// Implementasyon (CustomerSupportTeam) Adapters.Agents katmanında yaşar ve
/// Microsoft.Agents framework'üne bağımlıdır; bu port o detayları gizler.
/// </summary>
public interface IAgentTeamPort
{
    /// <summary>
    /// Kullanıcı sorgusunu workflow'da koşturur ve nihai yanıtı döndürür (non-streaming).
    /// </summary>
    Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null);

    /// <summary>
    /// Workflow'u SSE stream event'leri olarak koşturur.
    /// </summary>
    IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default);
}
