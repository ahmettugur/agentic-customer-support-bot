// Extensions/PersistenceServicesExtensions.cs
// Persistence adapter'larını kayıt eder. Tüm mantık Adapters.Persistence'e taşınmıştır.
using CustomerSupportBot.Adapters.Persistence.DependencyInjection;

namespace CustomerSupportBot.Api.Extensions;

public static class PersistenceServicesExtensions
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddPersistenceAdapters(configuration);
}

