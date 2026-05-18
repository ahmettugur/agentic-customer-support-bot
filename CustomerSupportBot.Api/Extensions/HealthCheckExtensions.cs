using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using PersistenceOptions = CustomerSupportBot.Api.Infrastructure.Persistence.PersistenceOptions;
using PersistenceProvider = CustomerSupportBot.Api.Infrastructure.Persistence.PersistenceProvider;

namespace CustomerSupportBot.Api.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddAppHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var persistence = configuration
            .GetSection(PersistenceOptions.SectionName)
            .Get<PersistenceOptions>() ?? new PersistenceOptions();

        var builder = services.AddHealthChecks();

        if (persistence.Provider == PersistenceProvider.Postgres)
        {
            builder.Add(new HealthCheckRegistration(
                "postgresql",
                sp => new PostgresHealthCheck(sp.GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>()),
                HealthStatus.Unhealthy,
                ["db", "ready"]));
        }

        builder.Add(new HealthCheckRegistration(
            "redis",
            sp => new RedisHealthCheck(sp.GetRequiredService<IConnectionMultiplexer>()),
            HealthStatus.Unhealthy,
            ["cache", "ready"]));

        return services;
    }

    // ── Liveness: /health/live ────────────────────────────────────────────────
    // Pod ayakta mı? DB/Redis'e dokunmaz, K8s restart kararı için.
    public static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder app)
    {
        // Liveness — yalnızca process sağlığı
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
           .WithTags("health")
           .AllowAnonymous();

        // Readiness — DB + Redis bağlantısı
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate        = hc => hc.Tags.Contains("ready"),
            ResponseWriter   = WriteJsonResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy]   = 200,
                [HealthStatus.Degraded]  = 200,
                [HealthStatus.Unhealthy] = 503,
            }
        }).AllowAnonymous();

        // Kısayol — her ikisini de kapsayan genel /health
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter  = WriteJsonResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy]   = 200,
                [HealthStatus.Degraded]  = 200,
                [HealthStatus.Unhealthy] = 503,
            }
        }).AllowAnonymous();

        return app;
    }

    private static Task WriteJsonResponse(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var entries = report.Entries.Select(e => new
        {
            name        = e.Key,
            status      = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs  = (int)e.Value.Duration.TotalMilliseconds,
            error       = e.Value.Exception?.Message
        });
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status     = report.Status.ToString(),
            totalMs    = (int)report.TotalDuration.TotalMilliseconds,
            checks     = entries
        });
        return ctx.Response.WriteAsync(result);
    }
}

// ── PostgreSQL health check ───────────────────────────────────────────────────
file sealed class PostgresHealthCheck(IDbContextFactory<CustomerSupportDbContext> factory) : IHealthCheck
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

// ── Redis health check ────────────────────────────────────────────────────────
file sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
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

