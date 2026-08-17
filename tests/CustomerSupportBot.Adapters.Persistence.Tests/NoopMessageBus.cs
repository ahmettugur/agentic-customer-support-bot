using CustomerSupportBot.Application.Ports.Outbound.Messaging;
// Tests/Helpers/NoopMessageBus.cs
// Test-only IMessageBusPort implementasyonu — Redis pub/sub'a bağımlı olmadan Postgres
// adaptörlerini (hydrate + cross-pod sync) izole test etmek için. Publish/Subscribe
// gerçek bir kanal kurmaz; adaptörlerin publish çağrılarının hata fırlatmadan
// çalıştığını doğrulamak için yeterlidir.

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public sealed class NoopMessageBus : IMessageBusPort
{
    public string NodeId { get; } = $"test-node-{Guid.NewGuid():N}";
    public void Publish(string channel, string jsonPayload) { }
    public void Subscribe(string channel, Action<string> handler) { }
}
