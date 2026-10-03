// Adapters.Redis/Idempotency/RedisIdempotencyStore.cs
// DRIVEN ADAPTER — IDistributedIdempotencyStore → Redis (SET NX PX).

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.Idempotency;

/// <summary>
/// Yan etkili tool çağrılarının pod'lar arası mükerrer kaydı. Senkron Redis API'si bilinçli:
/// tool'lar senkron çalışır ve StackExchange.Redis'in senkron çağrıları sync-over-async değil,
/// kütüphanenin birinci sınıf yoludur.
/// </summary>
public sealed class RedisIdempotencyStore : IDistributedIdempotencyStore
{
    private const string Prefix = "csbot:idempotency:";
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis) => _redis = redis;

    public string? Get(string key)
    {
        var value = _redis.GetDatabase().StringGet(Prefix + key);
        return value.HasValue ? value.ToString() : null;
    }

    // NX: ilk oluşturulan kayıt kanoniktir — sonraki bir yazma onun kimliğini değiştirmemeli.
    public void Set(string key, string entityId, TimeSpan ttl) =>
        _redis.GetDatabase().StringSet(Prefix + key, entityId, ttl, When.NotExists);
}
