// Adapters.Redis/HealthChecks/RedisHealthCheck.cs
// Redis sağlık denetimi — Ping komutu ile bağlantı kontrolü yapar.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.HealthChecks;

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis erişilebilir.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis erişilemiyor.", ex);
        }
    }
}
