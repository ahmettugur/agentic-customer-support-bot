// Adapters.Redis/DependencyInjection/RedisAdapterServiceCollectionExtensions.cs
// Redis bağlantısını ve distributed lock altyapısını DI'a kaydeder.
// Redis zorunludur — connection string yoksa uygulama başlatılmaz.
//
// Connection string öncelik sırası:
//   1) Redis:ConnectionString (override)
//   2) ConnectionStrings:Redis
//   3) Yoksa → InvalidOperationException

using CustomerSupportBot.Adapters.Redis.Locking;
using CustomerSupportBot.Adapters.Redis.Messaging;
using CustomerSupportBot.Application.Ports.Driven.Locking;
using CustomerSupportBot.Application.Ports.Driven.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.DependencyInjection;

public static class RedisAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddRedisAdapters(
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
            var logger = sp.GetRequiredService<ILogger<RedisDistributedLockAdapter>>();
            var configOptions = ConfigurationOptions.Parse(connectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy = new ExponentialRetry(5000);

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
        services.AddSingleton<IDistributedLockPort, RedisDistributedLockAdapter>();

        // Message bus — Redis pub/sub (yatay ölçeklendirme)
        services.AddSingleton<IMessageBusPort, RedisMessageBusAdapter>();

        return services;
    }
}
