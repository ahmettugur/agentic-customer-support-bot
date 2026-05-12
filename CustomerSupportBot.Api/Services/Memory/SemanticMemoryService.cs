// Services/Memory/SemanticMemoryService.cs
// Üst seviye memory facade'ı.
// Görevleri:
//   - Üç collection'ı (Episodic / Lessons / Knowledge) ensure-edip yönetmek
//   - WriteEpisodic / WriteLesson / SearchKnowledge gibi domain operasyonları sunmak
//   - Tek-noktadan embedding + upsert + search akışı

using CustomerSupportBot.Api.Models.Memory;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Services.Memory;

public sealed class SemanticMemoryService
{
    private readonly IVectorMemoryStore _store;
    private readonly IEmbeddingService _embedder;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<SemanticMemoryService> _logger;

    public bool Enabled => _options.Enabled;
    public bool IsConfigured => _embedder.IsConfigured;
    public SemanticMemoryOptions Options => _options;

    public SemanticMemoryService(
        IVectorMemoryStore store,
        IEmbeddingService embedder,
        IOptions<SemanticMemoryOptions> options,
        ILogger<SemanticMemoryService> logger)
    {
        _store = store;
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Tüm koleksiyonları yarat (idempotent). Startup'ta çağrılır.</summary>
    public async Task EnsureCollectionsAsync(CancellationToken ct = default)
    {
        if (!Enabled) return;
        await _store.EnsureCollectionAsync(_options.Collections.Episodic, _embedder.Dimension, ct);
        await _store.EnsureCollectionAsync(_options.Collections.Lessons, _embedder.Dimension, ct);
        await _store.EnsureCollectionAsync(_options.Collections.Knowledge, _embedder.Dimension, ct);
    }

    public string CollectionFor(MemoryKind kind) => kind switch
    {
        MemoryKind.Episodic => _options.Collections.Episodic,
        MemoryKind.Lesson => _options.Collections.Lessons,
        MemoryKind.Knowledge => _options.Collections.Knowledge,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Tek doküman yazar.</summary>
    public async Task UpsertAsync(MemoryDocument doc, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(doc.Text)) return;
        var vec = await _embedder.EmbedAsync(doc.Text, ct);
        await _store.UpsertAsync(CollectionFor(doc.Kind), [(doc, vec)], ct);
    }

    /// <summary>Toplu doküman yazımı (KB ingest gibi).</summary>
    public async Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
    {
        if (!Enabled || docs.Count == 0) return;

        var vectors = await _embedder.EmbedBatchAsync(docs.Select(d => d.Text).ToList(), ct);
        var pairs = docs.Zip(vectors, (d, v) => (Doc: d, Vector: v)).ToList();
        await _store.UpsertAsync(CollectionFor(kind), pairs, ct);

        _logger.LogInformation("Memory upsert: kind={Kind} count={Count}", kind, docs.Count);
    }

    /// <summary>Bir collection'da semantic search.</summary>
    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        MemoryKind kind, string query, int? topK = null, float? minScore = null,
        CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(query)) return Array.Empty<MemorySearchHit>();

        var vec = await _embedder.EmbedAsync(query, ct);
        return await _store.SearchAsync(
            CollectionFor(kind), vec,
            topK ?? _options.Retrieval.TopK,
            minScore ?? _options.Retrieval.MinScore,
            tagFilter: null,
            ct: ct);
    }

    /// <summary>Bir trace tamamlandığında çağrılır — episodik bellek yazımı.</summary>
    public Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
        string finalResponse, string? intent, int? rating, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(userQuery)) return Task.CompletedTask;

        var doc = new MemoryDocument
        {
            Kind = MemoryKind.Episodic,
            SessionId = sessionId,
            Source = traceId,
            Title = TruncateOneLine(userQuery, 80),
            Text = $"Soru: {userQuery}\n\nYanıt: {Truncate(finalResponse, 1200)}",
        };
        if (!string.IsNullOrEmpty(intent)) doc.Tags["intent"] = intent;
        if (rating.HasValue) doc.Tags["rating"] = rating.Value.ToString();
        return UpsertAsync(doc, ct);
    }

    public async Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
        => await _store.CountAsync(CollectionFor(kind), ct);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    private static string TruncateOneLine(string s, int max)
    {
        var line = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return line.Length <= max ? line : line[..max] + "…";
    }
}
