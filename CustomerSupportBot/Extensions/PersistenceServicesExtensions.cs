using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Persistence;

namespace CustomerSupportBot.Extensions;

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
            services.AddCustomerSupportPersistence(pgConnection);
            services.AddHostedService<PersistenceHydrator>();
        }

        // Reasoning trace store
        if (opts.Provider == PersistenceProvider.Postgres)
            services.AddSingleton<IReasoningTraceStore, PostgresReasoningTraceStore>();
        else
            services.AddSingleton<IReasoningTraceStore, InMemoryReasoningTraceStore>();

        // HITL stores
        services.Configure<ApprovalOptions>(configuration.GetSection("HumanInTheLoop"));
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
        }

        return services;
    }
}
