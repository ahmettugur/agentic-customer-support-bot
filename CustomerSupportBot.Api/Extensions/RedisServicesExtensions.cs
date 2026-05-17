// Extensions/RedisServicesExtensions.cs
// Redis bağlantısını ve distributed lock altyapısını DI'a kaydeder.
// Redis zorunludur — connection string yoksa uygulama başlatılmaz.
//
// Connection string öncelik sırası:
//   1) Redis:ConnectionString (override)
//   2) ConnectionStrings:Redis
//   3) Yoksa → InvalidOperationException

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Locking;
using StackExchange.Redis;

namespace CustomerSupportBot.Api.Extensions;

public static class RedisServicesExtensions
{
    public static IServiceCollection AddRedisServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(RedisOptions.SectionName);
        services.Configure<RedisOptions>(section);
        var opts = section.Get<RedisOptions>() ?? new RedisOptions();

        // Connection string çözümleme — Redis zorunlu
        var connectionString = opts.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Redis bağlantı dizesi bulunamadı. " +
                "Redis:ConnectionString veya ConnectionStrings:Redis tanımlayın. " +
                "Distributed lock için Redis zorunludur.");
        }

        // StackExchange.Redis — singleton multiplexer (thread-safe, tek bağlantı havuzu)
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisDistributedLock>>();
            var configOptions = ConfigurationOptions.Parse(connectionString);
            configOptions.AbortOnConnectFail = false; // graceful retry
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy = new ExponentialRetry(5000); // 5s base backoff

            var multiplexer = ConnectionMultiplexer.Connect(configOptions);

            multiplexer.ConnectionFailed += (_, args) =>
                logger.LogWarning("[Redis] Bağlantı koptu: {EndPoint} — {FailureType}",
                    args.EndPoint, args.FailureType);

            multiplexer.ConnectionRestored += (_, args) =>
                logger.LogInformation("[Redis] Bağlantı yeniden kuruldu: {EndPoint}",
                    args.EndPoint);

            return multiplexer;
        });

        // Distributed lock — Redis-backed (Medallion RedLock)
        services.AddSingleton<IAppDistributedLock, RedisDistributedLock>();

        return services;
    }
}
