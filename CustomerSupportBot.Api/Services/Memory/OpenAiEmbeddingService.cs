using CustomerSupportBot.Api.Models;
// Services/Memory/OpenAiEmbeddingService.cs
// OpenAI / Azure OpenAI embedding API tabanlı IEmbeddingService implementasyonu.
// AiOptions'tan provider'a göre client seçilir.

using System.ClientModel;
using Azure.AI.OpenAI;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace CustomerSupportBot.Api.Services.Memory;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient? _client;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public int Dimension { get; }
    public bool IsConfigured => _client is not null;

    public OpenAiEmbeddingService(
        IOptions<AiOptions> aiOptions,
        IOptions<SemanticMemoryOptions> memoryOptions,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _logger = logger;
        var ai = aiOptions.Value;
        var emb = memoryOptions.Value.Embedding;
        Dimension = emb.Dimension;

        // Embedding sadece OpenAI / Azure OpenAI üzerinden — Anthropic'te embedding yok.
        // Provider hangisi olursa olsun, key'i olan ilk client'ı seçiyoruz (fallback).
        // Hiçbiri yoksa _client null kalır; çağrı sırasında açıklayıcı hata atılır.
        try
        {
            var openAiKey = ai.OpenAI?.ApiKey;
            var azureKey = ai.AzureOpenAI?.ApiKey;
            var azureEndpoint = ai.AzureOpenAI?.Endpoint;

            // 1) Provider tercihine saygı göster
            if (ai.Provider == AiProvider.AzureOpenAI
                && !string.IsNullOrWhiteSpace(azureEndpoint)
                && !string.IsNullOrWhiteSpace(azureKey))
            {
                _client = new AzureOpenAIClient(new Uri(azureEndpoint!), new ApiKeyCredential(azureKey!))
                    .GetEmbeddingClient(emb.Model);
                _logger.LogInformation("Embedding client: AzureOpenAI (model={Model})", emb.Model);
            }
            else if (ai.Provider == AiProvider.OpenAI && !string.IsNullOrWhiteSpace(openAiKey))
            {
                _client = new OpenAIClient(openAiKey).GetEmbeddingClient(emb.Model);
                _logger.LogInformation("Embedding client: OpenAI (model={Model})", emb.Model);
            }
            // 2) Provider'da key yoksa diğerine fallback
            else if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                _client = new OpenAIClient(openAiKey).GetEmbeddingClient(emb.Model);
                _logger.LogInformation("Embedding client fallback: OpenAI (Provider={Provider})", ai.Provider);
            }
            else if (!string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureEndpoint))
            {
                _client = new AzureOpenAIClient(new Uri(azureEndpoint!), new ApiKeyCredential(azureKey!))
                    .GetEmbeddingClient(emb.Model);
                _logger.LogInformation("Embedding client fallback: AzureOpenAI (Provider={Provider})", ai.Provider);
            }
            else
            {
                _client = null;
                _logger.LogWarning("Semantic memory etkin ama hiçbir embedding ApiKey bulunamadı — memory devre dışı kalacak.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding client oluşturulamadı; memory devre dışı.");
            _client = null;
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_client is null)
            throw new InvalidOperationException("Embedding client yapılandırılmadı (OpenAI/AzureOpenAI ApiKey eksik).");
        if (string.IsNullOrWhiteSpace(text))
            return new float[Dimension];

        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct).ConfigureAwait(false);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();
        if (_client is null)
            throw new InvalidOperationException("Embedding client yapılandırılmadı (OpenAI/AzureOpenAI ApiKey eksik).");

        // OpenAI 1 isteğe maksimum ~2048 input alıyor; biz batch'leri 64'le sınırlayıp paralelliği basit tutuyoruz.
        const int batchSize = 64;
        var output = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var slice = texts.Skip(i).Take(batchSize).ToList();
            var resp = await _client.GenerateEmbeddingsAsync(slice, cancellationToken: ct).ConfigureAwait(false);
            output.AddRange(resp.Value.Select(e => e.ToFloats().ToArray()));
        }

        _logger.LogDebug("Embedded {Count} text(s) (model={Model}, dim={Dim})",
            texts.Count, "openai-embed", Dimension);

        return output;
    }
}

