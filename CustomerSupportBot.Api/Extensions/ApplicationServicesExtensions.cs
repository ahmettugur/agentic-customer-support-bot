using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Evaluation;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services.Providers;

namespace CustomerSupportBot.Api.Extensions;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
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

        // Prompt yükleyici
        services.AddSingleton<PromptService>();

        // HITL — approval gate + context accessor
        services.AddSingleton<ApprovalGateService>();
        services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();

        // Domain servisleri
        services.AddSingleton<EntityVerifier>();
        services.AddSingleton<ReasoningSanityChecker>();
        services.AddSingleton<ReasoningService>();
        services.AddSingleton<CustomerSupportTeam>();
        services.AddSingleton<ICustomerSupportTeam>(sp => sp.GetRequiredService<CustomerSupportTeam>());
        services.AddSingleton<EvaluationRunner>();

        // Chat orchestrators — istek başına yeni instance
        services.AddScoped<ChatStreamOrchestrator>();
        services.AddScoped<ChatEventOrchestrator>();

        // Realtime köprüsü — her WS bağlantısı için ayrı instance
        services.AddScoped<Services.Realtime.RealtimeBridge>();

        // Realtime "native" modu — gpt-realtime-2 kendisi konuşur ve okuma-only
        // tool'ları çağırır. Sipariş/şikayet gibi HITL gerektiren işlemler bu kanalda yok.
        services.AddSingleton<Services.Realtime.RealtimeFunctionTools>();
        services.AddScoped<Services.Realtime.RealtimeNativeBridge>();

        // Context provider'lar
        services.AddSingleton<IContextProvider, CustomerContextProvider>();
        services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
        services.AddSingleton<IContextProvider, CustomerProfileContextProvider>();
        services.AddSingleton<IContextProvider>(sp =>
        {
            // SemanticMemoryService opsiyonel — yoksa no-op provider üret
            var mem = sp.GetService<Services.Memory.SemanticMemoryService>();
            if (mem == null) return new NoopContextProvider();
            return new Services.Providers.SemanticMemoryContextProvider(
                mem,
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<ILogger<Services.Providers.SemanticMemoryContextProvider>>());
        });
        services.AddSingleton<ContextPipeline>();

        // Güvenlik — deterministik input gate
        services.AddSingleton<InputGuard>();

        services.AddSingleton<AnalyticsService>();

        // ─── Self-Improvement (LessonMiner) ───
        // ILessonStore → PersistenceServicesExtensions'da provider'a göre kaydedilir.
        services.AddSingleton<Services.Improvement.LessonMiner>();

        // ─── Per-Customer Personalization ───
        // ICustomerProfileStore → PersistenceServicesExtensions'da provider'a göre kaydedilir.
        services.AddSingleton<Services.Personalization.CustomerProfileService>();

        // ─── Smart Routing & Skills-Based Escalation (#11) ───
        // IHumanAgentRegistry → PersistenceServicesExtensions'da provider'a göre kaydedilir.
        services.AddSingleton<Services.Routing.ISkillsBasedRouter,
            Services.Routing.SkillsBasedRouter>();

        // ─── Low-Code Workflow Designer (#14) ───
        // IWorkflowDefinitionStore → PersistenceServicesExtensions'da provider'a göre kaydedilir.
        services.AddSingleton<Services.Workflow.WorkflowExecutor>();

        // ─── SLA / Response Time Guardian (#H) ───
        // ISlaEventSink → PersistenceServicesExtensions'da provider'a göre kaydedilir.
        services.AddHostedService<Services.Sla.SlaGuardianService>();

        return services;
    }
}
