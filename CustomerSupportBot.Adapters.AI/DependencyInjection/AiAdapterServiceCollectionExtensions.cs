// Adapters.AI/DependencyInjection/AiAdapterServiceCollectionExtensions.cs
// AI adapter'ları için DI kayıtları.
// IChatClient ve ReasoningChatClient'ın telemetri sarmalama işlemi Composition Root'a (Api) bırakılmıştır.

using CustomerSupportBot.Adapters.AI.OpenAi;
using CustomerSupportBot.Adapters.AI.Qdrant;
using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Domain.Model.Memory;
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
            services.AddSingleton<IEmbeddingPort, OpenAiEmbeddingAdapter>();
            services.AddSingleton<IVectorMemoryPort, QdrantVectorMemoryAdapter>();
        }

        return services;
    }
}
