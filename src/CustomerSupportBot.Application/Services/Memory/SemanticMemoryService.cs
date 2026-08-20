// Application/Services/Memory/SemanticMemoryService.cs
// Üst seviye memory facade'ı.
// Görevleri:
//   - Üç collection'ı (Episodic / Lessons / Knowledge) ensure-edip yönetmek
//   - WriteEpisodic / WriteLesson / SearchKnowledge gibi domain operasyonları sunmak
//   - Tek-noktadan embedding + upsert + search akışı

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed class SemanticMemoryService : ISemanticMemoryIngestor, ISemanticMemoryWriter
{
    private readonly IVectorMemoryPort _store;
    private readonly IEmbeddingPort _embedder;
    private readonly IContextSanitizer _sanitizer;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<SemanticMemoryService> _logger;

    public bool Enabled => _options.Enabled;
    public bool IsConfigured => _embedder.IsConfigured;
    public SemanticMemoryOptions Options => _options;

    public SemanticMemoryService(
        IVectorMemoryPort store,
        IEmbeddingPort embedder,
        IContextSanitizer sanitizer,
        IOptions<SemanticMemoryOptions> options,
        ILogger<SemanticMemoryService> logger)
    {
        _store = store;
        _embedder = embedder;
        _sanitizer = sanitizer;
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

    /// <inheritdoc />
    public async Task DeleteStaleAsync(
        MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default)
    {
        if (!Enabled) return;
        await _store.DeleteWhereTagNotAsync(CollectionFor(kind), tagKey, tagValue, ct);
    }

    /// <summary>Tek dokümanı indeksten siler.</summary>
    public async Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(documentId)) return;
        await _store.DeleteAsync(CollectionFor(kind), documentId, ct);
    }

    /// <summary>Bir collection'da semantic search.</summary>
    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        MemoryKind kind, string query, int? topK = null, float? minScore = null,
        CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(query)) return Array.Empty<MemorySearchHit>();

        var vec = await _embedder.EmbedAsync(query, ct);
        return await SearchByVectorAsync(kind, vec, topK, minScore, ct: ct);
    }

    /// <summary>
    /// Hazır bir sorgu vektörüyle arama yapar — embedding çağrısını atlar.
    ///
    /// <para>
    /// Aynı sorguyu birden fazla koleksiyonda aratan çağıranlar için: <see cref="SearchAsync"/>
    /// her çağrıda sorguyu yeniden embed ediyor. <c>SemanticMemoryContextProvider</c> her turda
    /// Knowledge ve Lesson koleksiyonlarını aynı kullanıcı mesajıyla arıyordu, yani tur başına
    /// AYNI metin iki kez embed ediliyordu (embedder'da cache yok). Bir kez embed edip bu
    /// metodu iki kez çağırmak embedding maliyetini yarıya indirir.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchHit>> SearchByVectorAsync(
        MemoryKind kind, float[] queryVector, int? topK = null, float? minScore = null,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default)
    {
        if (!Enabled || queryVector.Length == 0) return Array.Empty<MemorySearchHit>();

        return await _store.SearchAsync(
            CollectionFor(kind), queryVector,
            topK ?? _options.Retrieval.TopK,
            minScore ?? _options.Retrieval.MinScore,
            tagFilter: tagFilter,
            ct: ct);
    }

    /// <summary>Sorgu metnini vektöre çevirir — çok koleksiyonlu aramada tek sefer kullanılır.</summary>
    public Task<float[]> EmbedQueryAsync(string query, CancellationToken ct = default)
        => _embedder.EmbedAsync(query, ct);

    /// <summary>Bir trace tamamlandığında çağrılır — episodik bellek yazımı.</summary>
    public Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
        string finalResponse, string? intent, int? rating, string? customerId = null,
        CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(userQuery)) return Task.CompletedTask;

        // Write-time temizlik: episodik belleğe giren kullanıcı metni/yanıtı sanitize edilir
        // (read-time tarafında ayrıca <retrieved_data> fence uygulanır — çift katman).
        var safeQuery = _sanitizer.Sanitize(userQuery);
        var safeResponse = _sanitizer.Sanitize(finalResponse);

        var doc = new MemoryDocument
        {
            Kind = MemoryKind.Episodic,
            SessionId = sessionId,
            Source = traceId,
            Title = TruncateOneLine(safeQuery, 80),
            Text = $"Soru: {safeQuery}\n\nYanıt: {Truncate(safeResponse, 1200)}",
        };
        if (!string.IsNullOrEmpty(intent)) doc.Tags["intent"] = intent;
        if (rating.HasValue) doc.Tags["rating"] = rating.Value.ToString();
        // customerId tag olarak yazılır — retrieval'ın sessionId sınırını aşıp aynı müşterinin
        // FARKLI oturumlardaki geçmişini de bulabilmesi için. Anonim turlarda boş kalır; o
        // episode yalnızca sessionId ile bulunabilir olarak kalmaya devam eder.
        if (!string.IsNullOrWhiteSpace(customerId)) doc.Tags["customerId"] = customerId;
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
