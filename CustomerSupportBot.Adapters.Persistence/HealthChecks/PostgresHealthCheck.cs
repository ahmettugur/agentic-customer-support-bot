// Adapters.Persistence/HealthChecks/PostgresHealthCheck.cs
// PostgreSQL sağlık denetimi — "SELECT 1" ile bağlantı kontrolü yapar.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CustomerSupportBot.Adapters.Persistence.HealthChecks;

public sealed class PostgresHealthCheck(IDbContextFactory<CustomerSupportDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL erişilebilir.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL erişilemiyor.", ex);
        }
    }
}
