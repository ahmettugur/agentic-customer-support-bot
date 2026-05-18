// Adapters.AI/OpenAi/OpenAiEmbeddingAdapter.cs
// DRIVEN ADAPTER — IEmbeddingPort → OpenAI text-embedding implementasyonu.
// Core bu adapter'ı bilmez; sadece IEmbeddingPort'a bağımlıdır.

using Azure.AI.OpenAI;
using CustomerSupportBot.Application.Ports.Driven.AI;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace CustomerSupportBot.Adapters.AI.OpenAi;

/// <summary>
/// OpenAI / Azure OpenAI embedding adapter'ı.
/// SemanticMemoryService bu adapter aracılığıyla metin vektöre dönüştürür.
/// </summary>
public sealed class OpenAiEmbeddingAdapter : IEmbeddingPort
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingAdapter> _logger;
    private readonly int _dimension;

    public OpenAiEmbeddingAdapter(
        EmbeddingClient client,
        ILogger<OpenAiEmbeddingAdapter> logger,
        int dimension = 1536)
    {
        _client = client;
        _logger = logger;
        _dimension = dimension;
    }

    public int Dimension => _dimension;

    public bool IsConfigured => _client is not null;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var response = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);

        return response.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToArray();
    }
}
