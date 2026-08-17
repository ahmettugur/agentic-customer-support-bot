// Extensions/RedisServicesExtensions.cs
// Redis adapter'ını kayıt eder. Tüm mantık Adapters.Redis'e taşınmıştır.
using CustomerSupportBot.Adapters.Redis.DependencyInjection;

namespace CustomerSupportBot.Api.Extensions;

public static class RedisServicesExtensions
{
    public static IServiceCollection AddRedisServices(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddRedisAdapters(configuration);
}

