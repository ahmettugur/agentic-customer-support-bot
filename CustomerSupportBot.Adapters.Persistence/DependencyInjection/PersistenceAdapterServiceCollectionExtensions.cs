using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.Persistence.DependencyInjection;

public static class PersistenceAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPersistenceAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var persistenceSection = configuration.GetSection(PersistenceOptions.SectionName);
        services.Configure<PersistenceOptions>(persistenceSection);

        var pgConnection = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL tanımlı değil.");

        PersistenceServiceCollectionExtensions.AddCustomerSupportPersistence(services, pgConnection);
        services.AddHostedService<PersistenceHydrator>();

        services.AddScoped<IUserAuthRepository, EfUserAuthRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

        services.AddSingleton<IReasoningTraceStore, PostgresReasoningTraceStore>();
        services.AddSingleton<ILlmCallPersistencePort, PostgresLlmCallUsageSink>();
        services.AddSingleton<IApprovalQueue, PostgresApprovalQueue>();
        services.AddSingleton<IEscalationSink, PostgresEscalationSink>();
        services.AddSingleton<IChatModeRegistry, PostgresChatModeRegistry>();
        services.AddSingleton<IChatBridge, PostgresChatBridge>();
        services.AddSingleton<PostgresSessionManager>();
        services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<PostgresSessionManager>());
        services.AddSingleton<IRatingStore, PostgresRatingStore>();
        services.AddSingleton<IHumanAgentRegistry, PostgresHumanAgentRegistry>();
        services.AddSingleton<ICustomerProfileStore, PostgresCustomerProfileStore>();
        services.AddSingleton<ILessonStore, PostgresLessonStore>();
        services.AddSingleton<IWorkflowDefinitionStore, PostgresWorkflowDefinitionStore>();
        services.AddSingleton<ISlaEventSink, PostgresSlaEventSink>();

        services.AddSingleton<IOrderRepository, OrderRepository>();
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<IProductCatalogRepository, ProductCatalogRepository>();
        services.AddSingleton<IComplaintRepository, ComplaintRepository>();

        // Prompt şablonları — FileSystem adapter
        services.Configure<PromptOptions>(configuration.GetSection(PromptOptions.SectionName));
        services.AddSingleton<FileSystemPromptRepository>();
        services.AddSingleton<IPromptRepository>(sp =>
            sp.GetRequiredService<FileSystemPromptRepository>());

        // KnowledgeBase kaynak adaptörü
        services.AddSingleton<IKnowledgeBaseSource, FileSystemKnowledgeBaseSource>();

        return services;
    }
}
