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
        // Driving port implementasyonları
        services.AddScoped<ISessionPort, SessionPortService>();
        services.AddScoped<IApprovalPort, ApprovalPortService>();
        services.AddScoped<IEscalationPort, EscalationPortService>();
        services.AddScoped<IAnalyticsPort, AnalyticsPortService>();
        services.AddScoped<IChatSessionPort, ChatSessionPortService>();
        services.AddScoped<IHitlEventPort, HitlEventPortService>();
        services.AddScoped<ITelemetryPort, TelemetryPortService>();
        services.AddSingleton<ITracePort, TracePortService>();
        services.AddSingleton<IHumanAgentPort, HumanAgentPortService>();
        services.AddSingleton<IImprovementsPort, ImprovementsPortService>();
        services.AddSingleton<IPersonalizationPort, PersonalizationPortService>();
        services.AddSingleton<IWorkflowPort, WorkflowPortService>();
        services.AddSingleton<ISlaPort, SlaPortService>();

        // Use case servisleri
        services.AddSingleton<CustomerSupportToolsService>();
        services.AddSingleton<SubTaskOrchestrator>();
        services.AddSingleton<ReasoningMessageBuilder>();
        services.AddSingleton<ReasoningService>();
        services.AddSingleton<IReasoningPort>(sp => sp.GetRequiredService<ReasoningService>());
        services.AddSingleton<ChatPortService>();
        services.AddSingleton<IChatPort>(sp => sp.GetRequiredService<ChatPortService>());

        // Context Providers
        services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
        services.AddSingleton<IContextProvider, CustomerProfileContextProvider>();
        services.AddSingleton<IContextProvider, CustomerContextProvider>();
        services.AddSingleton<IContextProvider>(sp =>
        {
            // SemanticMemoryService opsiyonel — yoksa no-op provider üret
            var mem = sp.GetService<SemanticMemoryService>();
            if (mem == null) return new NoopContextProvider();
            return new SemanticMemoryContextProvider(
                mem,
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<ILogger<SemanticMemoryContextProvider>>());
        });
        services.AddSingleton<ContextPipeline>();

        // Approval context
        services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();

        // Replan use case
        services.AddSingleton<IReplanPort, ReplanService>();

        // Domain servisleri
        services.AddSingleton<EntityVerifier>();
        services.AddSingleton<ReasoningSanityChecker>();
        services.AddSingleton<EvaluationRunner>();
        services.AddSingleton<IEvaluationPort>(sp => sp.GetRequiredService<EvaluationRunner>());

        // Güvenlik — deterministik input gate
        services.AddSingleton<InputGuard>();
        services.AddSingleton<IInputGuard>(sp => sp.GetRequiredService<InputGuard>());

        // Session state yönetimi (sentiment, intent, persist)
        services.AddSingleton<SessionStateService>();

        // ─── Self-Improvement (LessonMiner) ───
        services.AddSingleton<Services.Improvement.LessonMiner>();

        // ─── Per-Customer Personalization ───
        services.AddSingleton<Services.Personalization.CustomerProfileService>();

        // ─── Smart Routing & Skills-Based Escalation ───
        services.AddSingleton<ISkillsBasedRouter, Services.Routing.SkillsBasedRouter>();

        // ─── Low-Code Workflow Designer ───
        services.AddSingleton<Services.Workflow.WorkflowExecutor>();

        // ─── Semantic Memory — IEmbeddingPort ve IVectorMemoryPort varsa aktif olur ───
        var memOpts = configuration.GetSection(SemanticMemoryOptions.SectionName)
                          .Get<SemanticMemoryOptions>() ?? new SemanticMemoryOptions();
        if (memOpts.Enabled)
        {
            services.AddSingleton<SemanticMemoryService>();
            services.AddSingleton<ISemanticMemoryIngestor>(sp => sp.GetRequiredService<SemanticMemoryService>());
            services.AddSingleton<IMemoryPort, MemoryPortService>();
        }
        else
        {
            services.AddSingleton<IMemoryPort, DisabledMemoryPort>();
        }

        return services;
    }
}
