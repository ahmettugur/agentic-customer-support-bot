// Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs
// Application katmanı servis kayıtları — driving port implementasyonları ve use case servisleri.

using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Routing;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.DependencyInjection;

/// <summary>
/// Application katmanı servislerini DI container'a kaydeder.
/// Driving port implementasyonları ve use case servisleri burada register edilir.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Application katmanı driving port implementasyonlarını ve use case servislerini kaydeder.
    /// </summary>
    public static IServiceCollection AddApplicationDrivingPorts(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApplicationOptions(configuration);
        services.AddDrivingPorts();
        services.AddChatServices();
        services.AddContextProviders();
        services.AddDomainServices();
        services.AddMemoryServices(configuration);

        return services;
    }

    private static void AddApplicationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApprovalOptions>(configuration.GetSection("HumanInTheLoop"));
        services.Configure<RoutingOptions>(configuration.GetSection("Routing"));
        services.Configure<ParallelExecutionOptions>(
            configuration.GetSection(ParallelExecutionOptions.SectionName));
        services.Configure<SlaOptions>(configuration.GetSection(SlaOptions.SectionName));
    }

    private static void AddDrivingPorts(this IServiceCollection services)
    {
        // Tüm driving port servisleri stateless — bağımlılıkları Singleton.
        // Singleton lifetime: tutarlı, gereksiz allokasyon yok, event subscription'lar tek sefer.

        // Realtime oturumları bağlantı başına durum taşıdığından Scoped kaydedilir.
        services.AddScoped<IRealtimeBridge, RealtimeBridgeService>();
        services.AddScoped<IRealtimeNativeBridge, RealtimeNativeService>();

        services.AddSingleton<ISessionPort, SessionPortService>();
        services.AddSingleton<IApprovalPort, ApprovalPortService>();
        services.AddSingleton<IEscalationPort, EscalationPortService>();
        services.AddSingleton<IAnalyticsPort, AnalyticsPortService>();
        services.AddSingleton<IChatSessionPort, ChatSessionPortService>();
        services.AddSingleton<IHitlEventPort, HitlEventPortService>();
        services.AddSingleton<ITelemetryPort, TelemetryPortService>();
        services.AddSingleton<ITracePort, TracePortService>();
        services.AddSingleton<IHumanAgentPort, HumanAgentPortService>();
        services.AddSingleton<IImprovementsPort, ImprovementsPortService>();
        services.AddSingleton<IPersonalizationPort, PersonalizationPortService>();
        services.AddSingleton<IWorkflowPort, WorkflowPortService>();
        services.AddSingleton<ISlaPort, SlaPortService>();
    }

    private static void AddChatServices(this IServiceCollection services)
    {
        services.AddSingleton<CustomerSupportToolsService>();
        services.AddSingleton<SubTaskOrchestrator>();
        services.AddSingleton<ReasoningMessageBuilder>();
        services.AddSingleton<ReasoningService>();
        services.AddSingleton<IReasoningPort>(sp => sp.GetRequiredService<ReasoningService>());
        services.AddSingleton<ChatPortService>();
        services.AddSingleton<IChatPort>(sp => sp.GetRequiredService<ChatPortService>());
        services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();
        services.AddSingleton<IReplanService, ReplanService>();
        services.AddSingleton<SessionStateService>();
    }

    private static void AddContextProviders(this IServiceCollection services)
    {
        services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
        services.AddSingleton<IContextProvider, CustomerProfileContextProvider>();
        services.AddSingleton<IContextProvider, CustomerContextProvider>();
        services.AddSingleton<ContextPipeline>();
    }

    private static void AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<EntityVerifier>();
        services.AddSingleton<ReasoningSanityChecker>();
        services.AddSingleton<EvaluationRunner>();
        services.AddSingleton<IEvaluationPort>(sp => sp.GetRequiredService<EvaluationRunner>());
        services.AddSingleton<InputGuard>();
        services.AddSingleton<IInputGuard>(sp => sp.GetRequiredService<InputGuard>());
        services.AddSingleton<Services.Improvement.LessonMiner>();
        services.AddSingleton<Services.Personalization.CustomerProfileService>();
        services.AddSingleton<ISkillsBasedRouter, Services.Routing.SkillsBasedRouter>();
        services.AddSingleton<Services.Workflow.WorkflowExecutor>();
    }

    private static void AddMemoryServices(this IServiceCollection services, IConfiguration configuration)
    {
        var memOpts = configuration.GetSection(SemanticMemoryOptions.SectionName)
                          .Get<SemanticMemoryOptions>() ?? new SemanticMemoryOptions();
        if (memOpts.Enabled)
        {
            services.AddSingleton<SemanticMemoryService>();
            services.AddSingleton<ISemanticMemoryIngestor>(sp => sp.GetRequiredService<SemanticMemoryService>());
            services.AddSingleton<KnowledgeBaseIngestionService>();
            services.AddSingleton<IKnowledgeBaseIngestor>(sp => sp.GetRequiredService<KnowledgeBaseIngestionService>());
            services.AddSingleton<IMemoryPort, MemoryPortService>();
            services.AddSingleton<IContextProvider, SemanticMemoryContextProvider>();
        }
        else
        {
            services.AddSingleton<ISemanticMemoryIngestor, DisabledSemanticMemoryIngestor>();
            services.AddSingleton<IMemoryPort, DisabledMemoryPort>();
            services.AddSingleton<IContextProvider, NoopContextProvider>();
        }
    }
}
