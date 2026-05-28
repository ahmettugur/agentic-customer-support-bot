// InMemory/InMemoryMessageBusAdapter.cs
// IMessageBusPort in-process implementasyonu.
// Tek pod senaryolarında pub/sub mesajlaşma ihtiyacı yoktur.
// Subscribe edilen handler'lar lokal Publish çağrısında tetiklenir.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryMessageBusAdapter : IMessageBusPort
{
    private readonly ConcurrentDictionary<string, List<Action<string>>> _handlers = new();

    public string NodeId { get; } = Guid.NewGuid().ToString("N");

    public void Publish(string channel, string jsonPayload)
    {
        if (_handlers.TryGetValue(channel, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try { handler(jsonPayload); }
                catch { /* InMemory — no-op on failure */ }
            }
        }
    }

    public void Subscribe(string channel, Action<string> handler)
    {
        _handlers.AddOrUpdate(
            channel,
            _ => new List<Action<string>> { handler },
            (_, existing) => { existing.Add(handler); return existing; });
    }
}
