using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CustomerSupportBot.Adapters.Agents.DependencyInjection;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.DependencyInjection;

namespace CustomerSupportBot.Api.Extensions;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ─── Hexagonal: Application katmanı servisleri ───
        services.AddApplicationDrivingPorts(configuration);

        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        services.AddCors(options =>
            options.AddDefaultPolicy(p =>
            {
                if (allowedOrigins is { Length: > 0 })
                    p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
                else
                    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }));

        // Enum'ları camelCase string olarak serialize et (ör. IssueSeverity.Warn → "warn")
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

        // Rate limiting: dakikada 20 istek per IP, kuyruk yok
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("chat", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        // Agents adapter — CustomerSupportTeam + ApprovalGateService
        services.AddAgentsAdapter();

        // Chat orchestrators — istek başına yeni instance (Api'ye özgü SSE/HTTP transport)
        services.AddScoped<ChatEventOrchestrator>();

        // Realtime köprüsü — her WS bağlantısı için ayrı instance
        services.AddScoped<Services.Realtime.RealtimeBridge>();

        // Realtime "native" modu — gpt-realtime-2 kendisi konuşur ve okuma-only
        // tool'ları çağırır. Sipariş/şikayet gibi HITL gerektiren işlemler bu kanalda yok.
        services.AddSingleton<Services.Realtime.RealtimeFunctionTools>();
        services.AddScoped<Services.Realtime.RealtimeNativeBridge>();

        return services;
    }
}
