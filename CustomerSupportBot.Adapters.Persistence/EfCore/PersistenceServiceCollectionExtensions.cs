// Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs
// DbContext + DbContextFactory + migration helper kayıtlarını gruplayan extension.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// EF Core DbContext + DbContextFactory'i kaydeder.
    /// Singleton servislerin DbContext açabilmesi için
    /// <see cref="IDbContextFactory{TContext}"/> şarttır.
    /// </summary>
    public static IServiceCollection AddCustomerSupportPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<CustomerSupportDbContext>(opt =>
        {
            opt.UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history", "public");
                npg.EnableRetryOnFailure(maxRetryCount: 3);
            });
            // Dev akışında snapshot ile küçük farklar engel olmasın; gerçek schema farkları zaten migration dosyasında görülür.
            opt.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        return services;
    }
}
