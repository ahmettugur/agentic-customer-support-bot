using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersistenceOptions = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceOptions;
using PersistenceProvider = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceProvider;

namespace CustomerSupportBot.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Development ortamında Postgres seçilmişse EF Core migration'larını uygular.
    /// Production'da migration ayrı bir deploy adımı olarak çalıştırılır.
    /// </summary>
    public static async Task MigrateIfDevelopmentAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        var opts = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        if (opts.Provider != PersistenceProvider.Postgres) return;

        await using var scope = app.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }
}

