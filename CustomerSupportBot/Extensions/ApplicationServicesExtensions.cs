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
        services.AddSingleton<CustomerSupportTeam>();
        services.AddSingleton<ICustomerSupportTeam>(sp => sp.GetRequiredService<CustomerSupportTeam>());
        services.AddSingleton<EvaluationRunner>();

        // Chat orchestrators — istek başına yeni instance
        services.AddScoped<ChatStreamOrchestrator>();
        services.AddScoped<ChatEventOrchestrator>();

        // Realtime köprüsü — her WS bağlantısı için ayrı instance
        services.AddScoped<Services.Realtime.RealtimeBridge>();

        // Realtime "native" modu — gpt-realtime-1.5 kendisi konuşur ve okuma-only
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
        // SelfImprovementOptions binding burada değil — AiServicesExtensions'ta IConfiguration var.
        services.AddSingleton<Services.Improvement.ILessonStore, Services.Improvement.InMemoryLessonStore>();
        services.AddSingleton<Services.Improvement.LessonMiner>();

        // ─── Per-Customer Personalization ───
        services.AddSingleton<Services.Personalization.ICustomerProfileStore,
            Services.Personalization.InMemoryCustomerProfileStore>();
        services.AddSingleton<Services.Personalization.CustomerProfileService>();

        // ─── Smart Routing & Skills-Based Escalation (#11) ───
        services.AddSingleton<Services.Routing.IHumanAgentRegistry,
            Services.Routing.InMemoryHumanAgentRegistry>();
        services.AddSingleton<Services.Routing.ISkillsBasedRouter,
            Services.Routing.SkillsBasedRouter>();

        // ─── Low-Code Workflow Designer (#14) ───
        services.AddSingleton<Services.Workflow.IWorkflowDefinitionStore,
            Services.Workflow.InMemoryWorkflowDefinitionStore>();
        services.AddSingleton<Services.Workflow.WorkflowExecutor>();

        // ─── SLA / Response Time Guardian (#H) ───
        services.AddSingleton<Services.Sla.ISlaEventSink,
            Services.Sla.InMemorySlaEventSink>();
        services.AddHostedService<Services.Sla.SlaGuardianService>();

        return services;
    }
}
