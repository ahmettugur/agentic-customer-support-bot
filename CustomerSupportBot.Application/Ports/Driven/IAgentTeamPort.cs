// Ports/Driven/IAgentTeamPort.cs
// SECONDARY PORT — Ajan takımının core'dan çağrılan sözleşmesi.
// ChatPortService, ReplanService, EvaluationRunner bu port'a bağımlıdır.
// Implementasyon (CustomerSupportTeam) Adapters.Agents katmanında yaşar;
// Microsoft.Agents framework bağımlılığı core'dan gizlenir.

using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Müşteri destek ajan takımının secondary (driven) port sözleşmesi.
/// Core bu port'a çağrı yapar; uygulama Adapters.Agents'ta çözümlenir.
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
