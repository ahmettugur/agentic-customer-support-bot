// Adapters.AI/Chat/ModelRoutingChatClient.cs
// Çağrıyı o anki tur katmanına göre standart ya da hızlı IChatClient'a yönlendirir. Hızlı istemci yoksa her şey standarttır.

using Microsoft.Extensions.AI;
using CustomerSupportBot.Application.Ports.Outbound.AI;

namespace CustomerSupportBot.Adapters.AI.Chat;

public sealed class ModelRoutingChatClient(IChatClient standard, IChatClient? fast, ITurnModelTier tier) : IChatClient
{
    private IChatClient Pick() => fast is not null && tier.Current == ModelTier.Fast ? fast : standard;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Pick().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Pick().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : standard.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        standard.Dispose();
        fast?.Dispose();
    }
}
