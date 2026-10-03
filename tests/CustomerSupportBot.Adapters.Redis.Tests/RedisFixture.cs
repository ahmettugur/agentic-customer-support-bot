// Tests/RedisFixture.cs
// Gerçek Redis (Testcontainers). Kilit, idempotency ve pub/sub adaptörleri sahte bir
// ikizle değil, üretimdeki sunucu semantiğiyle (SET NX PX, Lua tabanlı RedLock, PUBLISH)
// sınanır. Her "pod" ayrı bir ConnectionMultiplexer'dır.

using StackExchange.Redis;
using Testcontainers.Redis;

namespace CustomerSupportBot.Adapters.Redis.Tests;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private readonly List<ConnectionMultiplexer> _connections = new();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <summary>Yeni bir bağlantı — ayrı bir pod'u temsil eder.</summary>
    public async Task<IConnectionMultiplexer> ConnectAsync()
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        lock (_connections) _connections.Add(mux);
        return mux;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var mux in _connections) await mux.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
