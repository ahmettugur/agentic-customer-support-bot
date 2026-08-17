using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Müşteri destek ajan takımı için secondary (driven) port.
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
        ReasoningResult? reasoning = null,
        CancellationToken ct = default);

    /// <summary>
    /// Workflow'u SSE stream event'leri olarak koşturur.
    /// </summary>
    IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ajan takımı workflow graph'ının Mermaid.js diyagramını döner — dokümantasyon ve
    /// debug amaçlı (MAF'ın <c>Workflow.ToMermaidString()</c> extension'ı). Graph topolojisi
    /// tur/oturumdan bağımsız sabittir (aynı 6 ajan + GroupChatHost), bu yüzden argüman almaz.
    /// </summary>
    string GetWorkflowDiagram();
}
