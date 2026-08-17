using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Kullanıcı mesajını ön-analiz eden reasoning port'u.
/// </summary>
public interface IReasoningPort
{
    /// <summary>Non-streaming reasoning — tek ReasoningResult döndürür.</summary>
    Task<ReasoningResult> ReasonAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        CancellationToken ct = default);

    /// <summary>Streaming reasoning — delta event'leri yayar.</summary>
    IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        CancellationToken ct = default);
}
