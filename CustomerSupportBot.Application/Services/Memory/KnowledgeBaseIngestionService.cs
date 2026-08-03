// Application/Services/Memory/KnowledgeBaseIngestionService.cs
// KnowledgeBase ingest use case'inin Application katmanı implementasyonu.
// Dosya erişimi IKnowledgeBaseSource (driven port) üzerinden; vector store ISemanticMemoryIngestor üzerinden.
// Chunking algoritması ve change-detection orkestrasyonu burada kapsüllenir.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed class KnowledgeBaseIngestionService : IKnowledgeBaseIngestor
{
    private readonly IKnowledgeBaseSource _source;
    private readonly IKnowledgeArticleStore _articles;
    private readonly ISemanticMemoryIngestor _memory;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<KnowledgeBaseIngestionService> _logger;

    public KnowledgeBaseIngestionService(
        IKnowledgeBaseSource source,
        IKnowledgeArticleStore articles,
        ISemanticMemoryIngestor memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeBaseIngestionService> logger)
    {
        _source = source;
        _articles = articles;
        _memory = memory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task IngestAsync(CancellationToken ct = default)
    {
        if (!_memory.Enabled)
        {
            _logger.LogInformation("KnowledgeBase ingestion atlandı (semantic memory devre dışı).");
            return;
        }

        // Dizin yoksa dosya tarafı atlanır ama DB makaleleri yine indekslenmeli —
        // makaleler dosya sisteminden bağımsız bir kaynak.
        var hasFiles = _source.Exists;
        if (!hasFiles)
            _logger.LogWarning("KnowledgeBase dizini bulunamadı; yalnızca makaleler indekslenecek.");

        try
        {
            await _memory.EnsureCollectionsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VectorStore'a bağlanılamadı; KnowledgeBase ingest atlandı (servis çalışırken).");
            return;
        }

        if (!_memory.IsConfigured)
        {
            _logger.LogWarning("Embedding client yapılandırılmadı; KnowledgeBase ingest atlandı.");
            return;
        }

        // Panelden yönetilen makaleler. Normalde kaydedildikleri anda indekslenirler;
        // burada tekrar üretilmelerinin sebebi kurtarma senaryosu: embedding modeli
        // değişince collection sıfırdan yaratılır ve tüm kaynaklar yeniden yazılmalıdır.
        var articles = await _articles.GetPublishedAsync(ct);

        // Değişiklik parmakizi iki kaynağı da kapsamalı; yalnızca dizin hash'ine
        // bakılırsa makale değişikliği "değişmemiş" sayılır ve re-ingest sessizce atlanır.
        var lastHash    = _source.ReadStateHash();
        var currentHash = _source.ComputeDirectoryHash() + "|" + ComputeArticlesFingerprint(articles);
        if (lastHash == currentHash)
        {
            _logger.LogInformation("KnowledgeBase değişmemiş; ingestion atlandı.");
            return;
        }

        var docs = new List<MemoryDocument>();

        if (hasFiles)
        {
            await foreach (var file in _source.ReadFilesAsync(ct))
            {
                foreach (var (chunk, idx) in ChunkText(file.Content, _options.KnowledgeBase.ChunkSize, _options.KnowledgeBase.ChunkOverlap))
                {
                    docs.Add(new MemoryDocument
                    {
                        Kind   = MemoryKind.Knowledge,
                        Title  = file.Title,
                        Source = file.RelativePath,
                        Text   = chunk,
                        Tags   = { ["file"] = file.RelativePath, ["chunk"] = idx.ToString() }
                    });
                }
            }
        }

        foreach (var article in articles)
        {
            docs.AddRange(KnowledgeArticleService.BuildChunkDocuments(
                article, _options.KnowledgeBase.ChunkSize, _options.KnowledgeBase.ChunkOverlap));
        }

        if (articles.Count > 0)
            _logger.LogInformation("KnowledgeBase ingest: {ArticleCount} makale dahil edildi", articles.Count);

        _logger.LogInformation("KnowledgeBase ingest başlıyor: {ChunkCount} chunk", docs.Count);

        if (docs.Count > 0)
        {
            try
            {
                await _memory.UpsertManyAsync(MemoryKind.Knowledge, docs, ct);
                _source.WriteStateHash(currentHash);
                _logger.LogInformation("KnowledgeBase ingest tamamlandı: {ChunkCount} chunk", docs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KnowledgeBase ingest sırasında hata.");
            }
        }
    }

    /// <summary>
    /// Yayındaki makalelerin deterministik parmakizi (id + son güncelleme).
    /// Sıralama garanti edilir — store'un dönüş sırası değişse bile hash sabit kalmalı.
    /// Saf fonksiyon — I/O yok.
    /// </summary>
    internal static string ComputeArticlesFingerprint(IReadOnlyList<KnowledgeArticle> articles)
    {
        if (articles.Count == 0) return "articles=none";

        var sb = new StringBuilder("articles=");
        foreach (var a in articles.OrderBy(a => a.Id, StringComparer.Ordinal))
            sb.Append(a.Id).Append(':').Append(a.UpdatedAt.Ticks).Append(';');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Paragraf-aware, overlap'li metin parçalama. Saf algoritma — I/O yok.</summary>
    internal static IEnumerable<(string Chunk, int Index)> ChunkText(string text, int size, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        text = text.Replace("\r\n", "\n");

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        var sb = new StringBuilder();
        int idx = 0;
        foreach (var p in paragraphs)
        {
            if (sb.Length + p.Length + 2 > size && sb.Length > 0)
            {
                yield return (sb.ToString().Trim(), idx++);
                var carry = overlap > 0 && sb.Length > overlap ? sb.ToString()[^overlap..] : "";
                sb.Clear();
                if (carry.Length > 0) sb.AppendLine(carry);
            }
            sb.AppendLine(p);
            sb.AppendLine();
        }
        if (sb.Length > 0) yield return (sb.ToString().Trim(), idx);
    }
}
