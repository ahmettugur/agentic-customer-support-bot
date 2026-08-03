// Application/Services/Memory/KnowledgeArticleService.cs
// Panelden yönetilen bilgi tabanı makalelerinin use case'i.
//
// Kayıt otoritesi IKnowledgeArticleStore (Postgres); vector store türetilmiş indekstir.
// Bu yüzden sıra önemlidir: önce DB'ye yaz, sonra indeksle. İndeksleme patlarsa
// makale kaybolmaz — yalnızca "indekslenmedi" uyarısı döner ve admin tekrar kaydedebilir.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed class KnowledgeArticleService : IKnowledgeBasePort
{
    private readonly IKnowledgeArticleStore _store;
    private readonly ISemanticMemoryIngestor _memory;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<KnowledgeArticleService> _logger;

    public KnowledgeArticleService(
        IKnowledgeArticleStore store,
        ISemanticMemoryIngestor memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeArticleService> logger)
    {
        _store = store;
        _memory = memory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<KnowledgeArticle>> ListAsync(CancellationToken ct = default)
        => _store.GetAllAsync(ct);

    public Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)
        => _store.GetAsync(id, ct);

    public async Task<KnowledgeArticleSaveResult> CreateAsync(
        string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var article = new KnowledgeArticle
        {
            Title = title.Trim(),
            Content = content,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            IsPublished = isPublished,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = updatedBy
        };

        return await SaveAndReindexAsync(article, previousChunkCount: 0, ct);
    }

    public async Task<KnowledgeArticleSaveResult?> UpdateAsync(
        string id, string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(id, ct);
        if (existing is null) return null;

        var previousChunkCount = existing.IndexedChunkCount;

        existing.Title = title.Trim();
        existing.Content = content;
        existing.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        existing.IsPublished = isPublished;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = updatedBy;

        return await SaveAndReindexAsync(existing, previousChunkCount, ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(id, ct);
        if (existing is null) return false;

        // Önce indeksi temizle: DB satırı silinirse chunk id'lerini bir daha türetemeyiz
        // ve yetim vektörler ajanların yanıtlarını kirletmeye devam eder.
        await RemoveChunksAsync(id, from: 0, to: existing.IndexedChunkCount, ct);
        return await _store.DeleteAsync(id, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<KnowledgeArticleSaveResult> SaveAndReindexAsync(
        KnowledgeArticle article, int previousChunkCount, CancellationToken ct)
    {
        var docs = article.IsPublished
            ? BuildChunkDocuments(article, _options.KnowledgeBase.ChunkSize, _options.KnowledgeBase.ChunkOverlap)
            : [];

        if (!_memory.Enabled || !_memory.IsConfigured)
        {
            // İndeks yokken de makale kaydedilmeli; ama chunk sayısını gerçeğe uygun
            // tutmak için 0 yazıyoruz — sonraki bir kayıt yanlışlıkla "silinecek chunk" aramasın.
            article.IndexedChunkCount = 0;
            await _store.UpsertAsync(article, ct);
            return new KnowledgeArticleSaveResult(article, Indexed: false,
                Warning: "Semantic memory devre dışı veya yapılandırılmamış — makale kaydedildi ama aranabilir değil.");
        }

        article.IndexedChunkCount = docs.Count;
        await _store.UpsertAsync(article, ct);

        try
        {
            if (docs.Count > 0)
                await _memory.UpsertManyAsync(MemoryKind.Knowledge, docs, ct);

            // Kısalan (veya yayından kaldırılan) makalenin artık chunk'ları indekste kalmamalı.
            await RemoveChunksAsync(article.Id, from: docs.Count, to: previousChunkCount, ct);

            return new KnowledgeArticleSaveResult(article, Indexed: true, Warning: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KB] Makale indekslenemedi. Id={Id}", article.Id);

            // DB'deki chunk sayısı gerçeği yansıtmalı — indeks yazılamadıysa
            // eski sayıyı geri yaz ki sonraki kayıt doğru aralığı temizlesin.
            article.IndexedChunkCount = previousChunkCount;
            await _store.UpsertAsync(article, ct);

            return new KnowledgeArticleSaveResult(article, Indexed: false,
                Warning: "Makale kaydedildi ancak arama indeksine yazılamadı. Tekrar kaydetmeyi deneyin.");
        }
    }

    private async Task RemoveChunksAsync(string articleId, int from, int to, CancellationToken ct)
    {
        for (var i = from; i < to; i++)
            await _memory.DeleteAsync(MemoryKind.Knowledge, KnowledgeArticle.ChunkId(articleId, i), ct);
    }

    /// <summary>
    /// Makaleyi vector store dokümanlarına çevirir. Saf fonksiyon — I/O yok, test edilebilir.
    /// Chunk'lama dosya tabanlı KB ile <b>aynı</b> algoritmayı kullanır ki iki kaynak
    /// arama sonuçlarında tutarlı granülerlikte görünsün.
    /// </summary>
    internal static List<MemoryDocument> BuildChunkDocuments(KnowledgeArticle article, int chunkSize, int chunkOverlap)
    {
        var docs = new List<MemoryDocument>();

        foreach (var (chunk, idx) in KnowledgeBaseIngestionService.ChunkText(article.Content, chunkSize, chunkOverlap))
        {
            var doc = new MemoryDocument
            {
                Id = KnowledgeArticle.ChunkId(article.Id, idx),
                Kind = MemoryKind.Knowledge,
                Title = article.Title,
                Source = article.SourceRef,
                Text = chunk,
                CreatedAt = article.UpdatedAt,
                Tags =
                {
                    ["article"] = article.Id,
                    ["chunk"] = idx.ToString()
                }
            };

            if (!string.IsNullOrWhiteSpace(article.Category))
                doc.Tags["category"] = article.Category!;

            docs.Add(doc);
        }

        return docs;
    }
}
