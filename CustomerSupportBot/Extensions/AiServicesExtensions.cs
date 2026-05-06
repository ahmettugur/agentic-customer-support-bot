using CustomerSupportBot.Models;
using CustomerSupportBot.Models.Memory;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Memory;
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
            return AiClientFactory.CreateStandardChatClient(options);
        });

        services.AddSingleton<ReasoningChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateReasoningChatClient(options);
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
}
