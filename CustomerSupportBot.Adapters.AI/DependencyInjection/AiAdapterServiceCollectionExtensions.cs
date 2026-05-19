// Adapters.AI/DependencyInjection/AiAdapterServiceCollectionExtensions.cs
// AI adapter'ları için DI kayıtları.

using CustomerSupportBot.Adapters.AI.OpenAi;
using CustomerSupportBot.Adapters.AI.Qdrant;
using CustomerSupportBot.Application.Ports.Driven.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.AI.DependencyInjection;

/// <summary>
/// AI driven adapter'larını DI container'a kaydeder.
/// </summary>
public static class AiAdapterServiceCollectionExtensions
{
    /// <summary>
    /// OpenAI embedding ve Qdrant vector memory adapter'larını kaydeder.
    /// Adapter'lar IOptions&lt;AiOptions&gt; ve IOptions&lt;SemanticMemoryOptions&gt;'ı kullanır.
    /// </summary>
    public static IServiceCollection AddAiAdapters(this IServiceCollection services)
    {
        // IEmbeddingPort — OpenAI/AzureOpenAI embedding adapter (graceful fallback destekler)
        services.AddSingleton<IEmbeddingPort, OpenAiEmbeddingAdapter>();

        // IVectorMemoryPort — Qdrant adapter
        services.AddSingleton<IVectorMemoryPort, QdrantVectorMemoryAdapter>();

        return services;
    }
}
