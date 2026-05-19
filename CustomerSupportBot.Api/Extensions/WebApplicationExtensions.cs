using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersistenceOptions = CustomerSupportBot.Api.Infrastructure.Persistence.PersistenceOptions;
using PersistenceProvider = CustomerSupportBot.Api.Infrastructure.Persistence.PersistenceProvider;

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

    /// <summary>
    /// Smart Routing — Eskalasyon kapanınca (resolve/dismiss) atanan temsilcinin
    /// CurrentLoad'unu otomatik olarak -1 yapan event subscriber.
    /// </summary>
    public static WebApplication WireRoutingLoadTracking(this WebApplication app)
    {
        var sink = app.Services.GetRequiredService<IEscalationSink>();
        var registry = app.Services.GetRequiredService<IHumanAgentRegistry>();

        sink.RequestDecided += (_, esc) =>
        {
            // Sadece kapanmış (Resolved/Dismissed) eskalasyonlar için load azalt
            if (esc.Status is EscalationStatus.Resolved or EscalationStatus.Dismissed
                && !string.IsNullOrWhiteSpace(esc.SuggestedAgentId))
            {
                registry.DecrementLoad(esc.SuggestedAgentId);
            }
        };

        return app;
    }
}

