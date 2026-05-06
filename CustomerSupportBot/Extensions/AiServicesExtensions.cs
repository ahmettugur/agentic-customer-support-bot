using CustomerSupportBot.Models;
using CustomerSupportBot.Models.Memory;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Memory;
using CustomerSupportBot.Services.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Extensions;

public static class AiServicesExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var inner = AiClientFactory.CreateStandardChatClient(options);
            return WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());
        });

        services.AddSingleton<ReasoningChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateReasoningChatClient(options, inner =>
                WrapWithTelemetry(sp, inner, ResolveReasoningModel(options), options.Provider.ToString()));
        });

        // ─── Semantic Memory (Qdrant + embedding) ───
        services.Configure<SemanticMemoryOptions>(configuration.GetSection(SemanticMemoryOptions.SectionName));
        services.Configure<SelfImprovementOptions>(configuration.GetSection(SelfImprovementOptions.SectionName));
        var memOpts = configuration.GetSection(SemanticMemoryOptions.SectionName).Get<SemanticMemoryOptions>()
                      ?? new SemanticMemoryOptions();
        if (memOpts.Enabled)
        {
            services.AddSingleton<IEmbeddingService, OpenAiEmbeddingService>();
            services.AddSingleton<IVectorMemoryStore, QdrantVectorMemoryStore>();
            services.AddSingleton<SemanticMemoryService>();
            services.AddSingleton<KnowledgeBaseIngestor>();
            services.AddHostedService(sp => sp.GetRequiredService<KnowledgeBaseIngestor>());
        }

        return services;
    }

    private static IChatClient WrapWithTelemetry(IServiceProvider sp, IChatClient inner, string modelHint, string provider)
    {
        var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
        if (!telemetryOptions.Enabled)
        {
            return inner;
        }

        return new TelemetryChatClient(
            inner,
            sp.GetRequiredService<ICostCalculator>(),
            sp.GetRequiredService<CostUsageStore>(),
            modelHint,
            provider,
            sp.GetRequiredService<ILogger<TelemetryChatClient>>());
    }

    private static string ResolveStandardModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.Deployment ?? "(unknown)",
        AiProvider.Anthropic => options.Anthropic.Model ?? "(unknown)",
        _ => options.OpenAI.Model ?? "(unknown)"
    };

    private static string ResolveReasoningModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.ReasoningDeployment ?? options.AzureOpenAI.Deployment ?? "(unknown)",
        AiProvider.Anthropic => options.Anthropic.ReasoningModel ?? options.Anthropic.Model ?? "(unknown)",
        _ => options.OpenAI.ReasoningModel ?? options.OpenAI.Model ?? "(unknown)"
    };
}
