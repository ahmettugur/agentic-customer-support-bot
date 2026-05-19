using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using PersistenceOptions = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceOptions;
using PersistenceProvider = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceProvider;
using PersistenceServiceCollectionExtensions = CustomerSupportBot.Adapters.Persistence.EfCore.PersistenceServiceCollectionExtensions;

namespace CustomerSupportBot.Api.Extensions;

public static class PersistenceServicesExtensions
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var persistenceSection = configuration.GetSection(PersistenceOptions.SectionName);
        services.Configure<PersistenceOptions>(persistenceSection);
        var opts = persistenceSection.Get<PersistenceOptions>() ?? new PersistenceOptions();

        if (opts.Provider == PersistenceProvider.Postgres)
        {
            var pgConnection = configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException(
                    "Persistence:Provider=Postgres ancak ConnectionStrings:PostgreSQL tanımlı değil.");
            PersistenceServiceCollectionExtensions.AddCustomerSupportPersistence(services, pgConnection);
            services.AddHostedService<PersistenceHydrator>();
        }

        // Reasoning trace store
        if (opts.Provider == PersistenceProvider.Postgres)
            services.AddSingleton<IReasoningTraceStore, PostgresReasoningTraceStore>();
        else
            services.AddSingleton<IReasoningTraceStore, InMemoryReasoningTraceStore>();

        // HITL stores
        services.Configure<ApprovalOptions>(configuration.GetSection("HumanInTheLoop"));
        // Smart Routing — RoutingOptions binding (#11)
        services.Configure<RoutingOptions>(configuration.GetSection("Routing"));
        // Parallel Execution (#E) + SLA Guardian (#H)
        services.Configure<ParallelExecutionOptions>(
            configuration.GetSection(ParallelExecutionOptions.SectionName));
        services.Configure<SlaOptions>(configuration.GetSection(SlaOptions.SectionName));
        if (opts.Provider == PersistenceProvider.Postgres)
        {
            services.AddSingleton<IApprovalQueue, PostgresApprovalQueue>();
            services.AddSingleton<IEscalationSink, PostgresEscalationSink>();
            services.AddSingleton<IChatModeRegistry, PostgresChatModeRegistry>();
            services.AddSingleton<IChatBridge, PostgresChatBridge>();
            services.AddSingleton<PostgresSessionManager>();
            services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<PostgresSessionManager>());
            services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<PostgresSessionManager>());
            services.AddSingleton<IRatingStore, PostgresRatingStore>();
            services.AddSingleton<IHumanAgentRegistry, PostgresHumanAgentRegistry>();
            services.AddSingleton<ICustomerProfileStore, PostgresCustomerProfileStore>();
            services.AddSingleton<ILessonStore, PostgresLessonStore>();
            services.AddSingleton<IWorkflowDefinitionStore, PostgresWorkflowDefinitionStore>();
            services.AddSingleton<ISlaEventSink, PostgresSlaEventSink>();

            // Demo/test verileri — Postgres provider'da da InMemory adapter kullanılır
            // İleride gerçek EF Core entity'lerine dönüştürülebilir
            services.AddSingleton<IProductCatalogRepository, InMemoryProductCatalogAdapter>();
            services.AddSingleton<IOrderRepository, InMemoryOrderAdapter>();
            services.AddSingleton<IComplaintRepository, InMemoryComplaintAdapter>();
        }
        else
        {
            services.AddSingleton<IApprovalQueue, InMemoryApprovalQueue>();
            services.AddSingleton<IEscalationSink, InMemoryEscalationSink>();
            services.AddSingleton<IChatModeRegistry, InMemoryChatModeRegistry>();
            services.AddSingleton<IChatBridge, InMemoryChatBridge>();
            services.AddSingleton<InMemorySessionManager>();
            services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<InMemorySessionManager>());
            services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemorySessionManager>());
            services.AddSingleton<IRatingStore, InMemoryRatingStore>();
            services.AddSingleton<IHumanAgentRegistry, InMemoryHumanAgentRegistry>();
            services.AddSingleton<ICustomerProfileStore, InMemoryCustomerProfileStore>();
            services.AddSingleton<ILessonStore, InMemoryLessonStore>();
            services.AddSingleton<IWorkflowDefinitionStore, InMemoryWorkflowDefinitionStore>();
            services.AddSingleton<ISlaEventSink, InMemorySlaEventSink>();

            // Hexagonal: FakeDatabase'in yerini alan InMemory port adapter'lar�
            services.AddSingleton<IProductCatalogRepository, InMemoryProductCatalogAdapter>();
            services.AddSingleton<IOrderRepository, InMemoryOrderAdapter>();
            services.AddSingleton<IComplaintRepository, InMemoryComplaintAdapter>();
        }

        return services;
    }
}

