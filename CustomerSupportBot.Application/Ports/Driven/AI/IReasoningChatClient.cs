// Application/Ports/Driven/AI/IReasoningChatClient.cs
// SECONDARY PORT — Reasoning model (o-series) için ayrılmış chat client.
// Implementasyon Adapters.AI katmanında; Application katmanı bu arayüze bağımlıdır.

using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// Reasoning model (o-series) için ayrılmış IChatClient wrapper'ının secondary port sözleşmesi.
/// </summary>
public interface IReasoningChatClient
{
    /// <summary>Reasoning çağrıları için kullanılacak chat client.</summary>
    IChatClient Client { get; }

    /// <summary>Kullanılan modelin adı.</summary>
    string ModelName { get; }

    /// <summary>Reasoning effort seviyesi (low / medium / high).</summary>
    string ReasoningEffort { get; }
}
