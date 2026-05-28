using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <summary>
/// Reasoning model için secondary (driven) port.
/// Framework'e özgü IChatClient ve ChatMessage tiplerini core'dan gizler.
/// </summary>
public interface IReasoningChatClient
{
    /// <summary>Kullanılan modelin adı.</summary>
    string ModelName { get; }

    /// <summary>Reasoning effort seviyesi (low / medium / high).</summary>
    string ReasoningEffort { get; }

    /// <summary>Tek seferlik reasoning tamamlama — tam metni döner.</summary>
    Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken ct = default);

    /// <summary>Streaming reasoning — token parçaları döner.</summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken ct = default);
}
