using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CustomerSupportBot.Adapters.Agents.DependencyInjection;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.DependencyInjection;

using System.Security.Claims;

using CustomerSupportBot.Application.Services.A2A;

using Microsoft.Extensions.Options;

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

        // Rate limiting: "chat" = 20/dk, "general" = 60/dk (public rating endpoint'leri dahil)
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
            options.AddPolicy("general", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // A2A: bölümleme IP'ye DEĞİL PARTNER'a göre yapılır. Dış sistemler proxy/bulut
            // çıkışı arkasında IP paylaşabilir (bir partnerin trafiği diğerinin sınırını
            // tüketirdi) ya da IP değiştirebilir (sınır fiilen ortadan kalkardı). Kimlik
            // token'dan gelir ve çağıran onu değiştiremez.
            //
            // Özne token'ında partner, kimliğin içindedir; oradan çıkarılır ki bir partner
            // çok sayıda müşteri adına çağrı yaparak servisi tek başına tüketemesin.
            options.AddPolicy("a2a", httpContext =>
            {
                var limits = httpContext.RequestServices
                    .GetRequiredService<IOptions<A2AOptions>>().Value;

                var subjectId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var partitionKey =
                    A2ASubjectIdentity.TryGetPartnerId(subjectId)   // özne token'ı → partner
                    ?? subjectId                                    // partner token'ı → kendisi
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()  // kimliksiz → IP
                    ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"a2a:{partitionKey}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, limits.RequestsPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        // Agents adapter — CustomerSupportTeam + ApprovalGateService
        services.AddAgentsAdapter();

        // Chat orchestrators — istek başına yeni instance (Api'ye özgü SSE/HTTP transport)
        services.AddScoped<ChatEventOrchestrator>();

        // ─── Background Workers (hosting adapter) ───
        services.AddHostedService<SlaGuardianService>();
        // RoutingLoadTrackerService kaldırıldı — load-tracking HumanAgentPortService constructor'ında.

        // KnowledgeBase startup adapter — use-case mantığı Application katmanında; bu sadece startup tetikleyicisi
        services.AddHostedService<KnowledgeBaseStartupService>();

        return services;
    }
}
