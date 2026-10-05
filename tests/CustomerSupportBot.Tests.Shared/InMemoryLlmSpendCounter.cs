// Test-only ILlmSpendCounter — Redis sayaçlarının süreç içi karşılığı (TTL tutulur ama uygulanmaz).

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Tests.Shared;

public sealed class InMemoryLlmSpendCounter : ILlmSpendCounter
{
    private readonly ConcurrentDictionary<string, decimal> _values = new();
    public ConcurrentDictionary<string, TimeSpan> Ttls { get; } = new();

    /// <summary>Doluysa her çağrı bu hatayı fırlatır (Redis kesintisi).</summary>
    public Exception? Failure { get; set; }

    public Task<decimal?> GetAsync(string key, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult(_values.TryGetValue(key, out var v) ? v : (decimal?)null);
    }

    public Task<decimal> AddAsync(string key, decimal amount, TimeSpan ttl, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        Ttls.TryAdd(key, ttl);
        return Task.FromResult(_values.AddOrUpdate(key, amount, (_, v) => v + amount));
    }

    public Task SeedAsync(string key, decimal value, TimeSpan ttl, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        if (_values.TryAdd(key, value)) Ttls.TryAdd(key, ttl);
        return Task.CompletedTask;
    }
}
