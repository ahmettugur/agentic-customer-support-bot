// Adapters.AI/DependencyInjection/AiAdapterServiceCollectionExtensions.cs
// AI adapter'ları için DI kayıtları.
// IChatClient ve ReasoningChatClient'ın telemetri sarmalama işlemi Composition Root'a (Api) bırakılmıştır.

using CustomerSupportBot.Adapters.AI.OpenAi;
using CustomerSupportBot.Adapters.AI.Qdrant;
using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.AI.DependencyInjection;

/// <summary>
/// AI driven adapter'larını DI container'a kaydeder.
/// </summary>
public static class AiAdapterServiceCollectionExtensions
{
    /// <summary>
    /// AiOptions bağlaması, embedding ve vector memory adapter'larını kaydeder.
    /// IChatClient ve ReasoningChatClient kaydı (telemetri sarmalama dahil)
    /// Composition Root'ta (Api/Extensions/AiServicesExtensions) yapılır.
    /// </summary>
    public static IServiceCollection AddAiAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<SemanticMemoryOptions>(configuration.GetSection(SemanticMemoryOptions.SectionName));
        services.Configure<SelfImprovementOptions>(configuration.GetSection(SelfImprovementOptions.SectionName));

        // Realtime: tool şemaları Singleton, WS client bağlantı başına Scoped.
        services.AddSingleton<RealtimeFunctionTools>();
        services.AddScoped<IRealtimeVoiceTransport, OpenAiRealtimeClientAdapter>();

        var memOpts = configuration.GetSection(SemanticMemoryOptions.SectionName)
                          .Get<SemanticMemoryOptions>() ?? new SemanticMemoryOptions();

        if (memOpts.Enabled)
        {
            services.AddSingleton<OpenAiEmbeddingAdapter>();
            services.AddSingleton<IEmbeddingPort>(sp =>
            {
                var emb = sp.GetRequiredService<IOptions<SemanticMemoryOptions>>().Value.Embedding;
                return new CachingEmbeddingPort(sp.GetRequiredService<OpenAiEmbeddingAdapter>(),
                    emb.QueryCacheSize, TimeSpan.FromMinutes(Math.Max(1, emb.QueryCacheMinutes)));
            });
            services.AddSingleton<IVectorMemoryPort, QdrantVectorMemoryAdapter>();
        }

        return services;
    }
}
