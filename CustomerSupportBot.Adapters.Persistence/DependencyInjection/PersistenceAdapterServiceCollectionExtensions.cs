// Adapters.Persistence/DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs
// Tüm persistence adapter DI kayıtları burada toplanmıştır.
// InMemory ve Postgres provider seçimi bu katmanda yapılır.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Auth;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Model;
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
        var opts = persistenceSection.Get<PersistenceOptions>() ?? new PersistenceOptions();

        services.Configure<ApprovalOptions>(configuration.GetSection("HumanInTheLoop"));
        services.Configure<RoutingOptions>(configuration.GetSection("Routing"));
        services.Configure<ParallelExecutionOptions>(
            configuration.GetSection(ParallelExecutionOptions.SectionName));
        services.Configure<SlaOptions>(configuration.GetSection(SlaOptions.SectionName));

        if (opts.Provider == PersistenceProvider.Postgres)
        {
            var pgConnection = configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException(
                    "Persistence:Provider=Postgres ancak ConnectionStrings:PostgreSQL tanımlı değil.");

            PersistenceServiceCollectionExtensions.AddCustomerSupportPersistence(services, pgConnection);
            services.AddHostedService<PersistenceHydrator>();

            services.AddScoped<IUserAuthRepository, EfUserAuthRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

            services.AddSingleton<IReasoningTraceStore, PostgresReasoningTraceStore>();
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
        }
        else
        {
            services.AddSingleton<IReasoningTraceStore, InMemoryReasoningTraceStore>();
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
        }

        // Demo/test katalog verileri — her iki provider'da da InMemory
        services.AddSingleton<IProductCatalogRepository, InMemoryProductCatalogAdapter>();
        services.AddSingleton<IOrderRepository, InMemoryOrderAdapter>();
        services.AddSingleton<IComplaintRepository, InMemoryComplaintAdapter>();

        // Prompt şablonları — FileSystem adapter, her iki provider'da aynı
        services.AddSingleton<FileSystemPromptRepository>();
        services.AddSingleton<IPromptRepository>(sp =>
            sp.GetRequiredService<FileSystemPromptRepository>());

        return services;
    }
}
