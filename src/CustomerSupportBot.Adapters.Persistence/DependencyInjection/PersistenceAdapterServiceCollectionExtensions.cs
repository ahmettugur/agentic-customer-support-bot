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
        services.AddHostedService<DemoDataSeeder>();
        services.AddHostedService<StaleApprovalSweepService>();

        services.AddScoped<IUserAuthRepository, EfUserAuthRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

        services.AddSingleton<ILlmCallPersistencePort, PostgresLlmCallUsageSink>();
        services.AddSingleton<IApprovalQueue, PostgresApprovalQueue>();
        services.AddSingleton<PostgresSessionManager>();
        services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<PostgresSessionManager>());
        services.AddSingleton<IHumanAgentRegistry, PostgresHumanAgentRegistry>();
        services.AddSingleton<ICustomerProfileStore, PostgresCustomerProfileStore>();
        services.AddSingleton<ILessonStore, PostgresLessonStore>();
        services.AddSingleton<ISlaEventSink, PostgresSlaEventSink>();
        services.AddSingleton<IKnowledgeArticleStore, PostgresKnowledgeArticleStore>();

        // Oturuma bağlı kişisel veri tutan depolar: aynı tekil örnek hem kendi portu hem de
        // ISessionDataEraser olarak çözülür (KVKK silmesi — bkz. DataPrivacyService). Önbellek ısıtma
        // (PersistenceHydrator) portu çözdüğü için örnek tek kalmalı.
        AddSessionDataStore<IReasoningTraceStore, PostgresReasoningTraceStore>(services);
        AddSessionDataStore<IEscalationSink, PostgresEscalationSink>(services);
        AddSessionDataStore<IChatModeRegistry, PostgresChatModeRegistry>(services);
        AddSessionDataStore<IChatBridge, PostgresChatBridge>(services);
        AddSessionDataStore<IRatingStore, PostgresRatingStore>(services);
        AddSessionDataStore<IAttachmentStore, PostgresAttachmentStore>(services);
        AddSessionDataStore<IVoiceCallStore, PostgresVoiceCallStore>(services);
        AddSessionDataStore<IVoiceRecordingStore, PostgresVoiceRecordingStore>(services);
        AddSessionDataStore<IConversationDispositionStore, PostgresConversationDispositionStore>(services);

        services.AddSingleton<INotificationLedger, PostgresNotificationLedger>();
        services.AddSingleton<ISavedReplyStore, PostgresSavedReplyStore>();
        services.AddSingleton<IConversationSearchStore, PostgresConversationSearchStore>();
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

    private static void AddSessionDataStore<TPort, TImpl>(IServiceCollection services)
        where TPort : class
        where TImpl : class, TPort, ISessionDataEraser
    {
        services.AddSingleton<TImpl>();
        services.AddSingleton<TPort>(sp => sp.GetRequiredService<TImpl>());
        services.AddSingleton<ISessionDataEraser>(sp => sp.GetRequiredService<TImpl>());
    }
}
