using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CustomerSupportBot.Agents;
using CustomerSupportBot.Evaluation;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Providers;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerSupportBot.Extensions;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddCors(options =>
            options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

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

        // Prompt yükleyici
        services.AddSingleton<PromptService>();

        // HITL — approval gate + context accessor
        services.AddSingleton<ApprovalGateService>();
        services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();

        // Domain servisleri
        services.AddSingleton<EntityVerifier>();
        services.AddSingleton<ReasoningSanityChecker>();
        services.AddSingleton<ReasoningService>();
        services.AddSingleton<RevisionService>();
        services.AddSingleton<CustomerSupportTeam>();
        services.AddSingleton<ICustomerSupportTeam>(sp => sp.GetRequiredService<CustomerSupportTeam>());
        services.AddSingleton<EvaluationRunner>();

        // Chat orchestrators — istek başına yeni instance
        services.AddScoped<ChatStreamOrchestrator>();
        services.AddScoped<ChatEventOrchestrator>();

        // Context provider'lar
        services.AddSingleton<IContextProvider, CustomerContextProvider>();
        services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
        services.AddSingleton<ContextPipeline>();

        // Güvenlik — deterministik input gate
        services.AddSingleton<InputGuard>();

        services.AddSingleton<AnalyticsService>();

        return services;
    }
}
