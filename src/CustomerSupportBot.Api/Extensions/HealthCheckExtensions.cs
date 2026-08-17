// Extensions/HealthCheckExtensions.cs
// Sağlık denetimleri: implementasyonlar Adapters.Persistence ve Adapters.Redis'e taşındı.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.HealthChecks;
using CustomerSupportBot.Adapters.Redis.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using PersistenceOptions = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceOptions;
using PersistenceProvider = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceProvider;

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

    public static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
           .WithTags("health")
           .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate         = hc => hc.Tags.Contains("ready"),
            ResponseWriter    = WriteJsonResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy]   = 200,
                [HealthStatus.Degraded]  = 200,
                [HealthStatus.Unhealthy] = 503,
            }
        }).AllowAnonymous();

        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter    = WriteJsonResponse,
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
            status  = report.Status.ToString(),
            totalMs = (int)report.TotalDuration.TotalMilliseconds,
            checks  = entries
        });
        return ctx.Response.WriteAsync(result);
    }
}

