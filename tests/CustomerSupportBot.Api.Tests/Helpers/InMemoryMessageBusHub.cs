using CustomerSupportBot.Application.Ports.Outbound.Messaging;
// Tests/Helpers/InMemoryMessageBusHub.cs
// Cross-pod senkronizasyon testleri için gerçek pub/sub davranışı sergileyen test double.
// NoopMessageBus'tan farkı: Publish edilen mesaj gerçekten dinleyen tüm Node'lara ulaşır —
// böylece "pod A yazar, pod B'nin cache'i güncellenir" senaryosu gerçekten kanıtlanabilir.
// Her Node kendi benzersiz NodeId'sine sahiptir (Redis'teki gibi) — adaptörlerin kendi
// yayınladığı mesajı atlama mantığı (nodeId karşılaştırması) da bu sayede test edilir.

namespace CustomerSupportBot.Api.Tests.Helpers;

public sealed class InMemoryMessageBusHub
{
    private readonly Dictionary<string, List<Action<string>>> _handlers = new();
    private readonly Lock _lock = new();

    /// <summary>Hub'a bağlı, kendi NodeId'sine sahip yeni bir "pod" oluşturur.</summary>
    public IMessageBusPort CreateNode() => new Node(this);

    private void Publish(string channel, string jsonPayload)
    {
        List<Action<string>>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(channel, out handlers)) return;
            handlers = [.. handlers];
        }
        foreach (var handler in handlers) handler(jsonPayload);
    }

    private void Subscribe(string channel, Action<string> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(channel, out var list))
                _handlers[channel] = list = [];
            list.Add(handler);
        }
    }

    private sealed class Node(InMemoryMessageBusHub hub) : IMessageBusPort
    {
        public string NodeId { get; } = Guid.NewGuid().ToString("N");
        public void Publish(string channel, string jsonPayload) => hub.Publish(channel, jsonPayload);
        public void Subscribe(string channel, Action<string> handler) => hub.Subscribe(channel, handler);
    }
}
