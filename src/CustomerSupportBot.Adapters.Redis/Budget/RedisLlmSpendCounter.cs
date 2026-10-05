// Adapters.Redis/Budget/RedisLlmSpendCounter.cs
// DRIVEN ADAPTER — ILlmSpendCounter → Redis (INCRBYFLOAT + ilk yazımda TTL, tek Lua betiği).

using System.Globalization;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.Budget;

/// <summary>
/// Pod'lar arası LLM harcama sayacı. Artış ve TTL tek betikte, atomik: ayrı iki komut olsaydı arada çöken
/// bir pod TTL'siz (hiç düşmeyen) bir dönem anahtarı bırakabilirdi. TTL yalnızca anahtarda TTL yokken konur;
/// her artışta uzatılsaydı dönem anahtarı dönem bitince düşmezdi.
/// </summary>
public sealed class RedisLlmSpendCounter(IConnectionMultiplexer redis) : ILlmSpendCounter
{
    public const string Prefix = "csbot:";

    private const string AddScript = """
        local v = redis.call('INCRBYFLOAT', KEYS[1], ARGV[1])
        if redis.call('PTTL', KEYS[1]) < 0 then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        return v
        """;

    public async Task<decimal?> GetAsync(string key, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Prefix + key);
        return value.HasValue ? Parse(value!) : null;
    }

    public async Task<decimal> AddAsync(string key, decimal amount, TimeSpan ttl, CancellationToken ct = default)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(AddScript,
            [Prefix + key],
            [amount.ToString(CultureInfo.InvariantCulture), (long)ttl.TotalMilliseconds]);
        return Parse(result.ToString()!);
    }

    public Task SeedAsync(string key, decimal value, TimeSpan ttl, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(Prefix + key, value.ToString(CultureInfo.InvariantCulture), ttl, When.NotExists);

    private static decimal Parse(string s) => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
