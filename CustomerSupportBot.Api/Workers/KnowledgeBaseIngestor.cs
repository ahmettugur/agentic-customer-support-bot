// Api/Workers/KnowledgeBaseIngestor.cs
// KnowledgeBase/*.md dosyalarını chunk'lara böler, embed eder, Qdrant Knowledge collection'a yazar.
// Hash tabanlı change detection: her dosya için son işlenen hash'i hatırlar; değişmediyse atlar.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Application.Ports.Driving;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Startup hosted service — KnowledgeBase dizinini Qdrant'a senkronize eder.
/// Yapılandırma: SemanticMemory:KnowledgeBase:AutoIngestOnStartup
/// </summary>
public sealed class KnowledgeBaseIngestor : IHostedService, IKnowledgeBaseIngestor
{
    private readonly ISemanticMemoryIngestor _memory;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<KnowledgeBaseIngestor> _logger;
    private readonly string _rootDir;
    private readonly string _stateFile;

    public KnowledgeBaseIngestor(
        ISemanticMemoryIngestor memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeBaseIngestor> logger)
    {
        _memory = memory;
        _options = options.Value;
        _logger = logger;
        _rootDir = Path.Combine(AppContext.BaseDirectory, "KnowledgeBase");
        _stateFile = Path.Combine(AppContext.BaseDirectory, ".kb-ingest-state.txt");
    }

    public Task StartAsync(CancellationToken cancellationToken) => IngestAsync(cancellationToken);

    public async Task IngestAsync(CancellationToken ct = default)
    {
        if (!_memory.Enabled || !_options.KnowledgeBase.AutoIngestOnStartup)
        {
            _logger.LogInformation("KnowledgeBase ingestion atlandı (disabled).");
            return;
        }

        try
        {
            await _memory.EnsureCollectionsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant'a bağlanılamadı; semantic memory devre dışı kalacak (servis çalışırken).");
            return;
        }

        if (!Directory.Exists(_rootDir))
        {
            _logger.LogInformation("KnowledgeBase dizini bulunamadı, oluşturuluyor: {Path}", _rootDir);
            Directory.CreateDirectory(_rootDir);
            return;
        }

        var lastHash = ReadStateHash();
        var currentHash = ComputeDirectoryHash();
        if (lastHash == currentHash)
        {
            _logger.LogInformation("KnowledgeBase değişmemiş; ingestion atlandı.");
            return;
        }

        if (!_memory.IsConfigured)
        {
            _logger.LogWarning("Embedding client yapılandırılmadı; KnowledgeBase ingest atlandı (memory devre dışı).");
            return;
        }

        var files = Directory.EnumerateFiles(_rootDir, "*.md", SearchOption.AllDirectories).ToList();
        _logger.LogInformation("KnowledgeBase ingest başlıyor: {Count} dosya", files.Count);

        var docs = new List<Domain.Model.Memory.MemoryDocument>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            var relative = Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
            var title = Path.GetFileNameWithoutExtension(file);
            foreach (var (chunk, idx) in ChunkText(text, _options.KnowledgeBase.ChunkSize, _options.KnowledgeBase.ChunkOverlap))
            {
                docs.Add(new Domain.Model.Memory.MemoryDocument
                {
                    Kind = Domain.Model.Memory.MemoryKind.Knowledge,
                    Title = title,
                    Source = relative,
                    Text = chunk,
                    Tags = { ["file"] = relative, ["chunk"] = idx.ToString() }
                });
            }
        }

        if (docs.Count > 0)
        {
            try
            {
                await _memory.UpsertManyAsync(Domain.Model.Memory.MemoryKind.Knowledge, docs, ct);
                File.WriteAllText(_stateFile, currentHash);
                _logger.LogInformation("KnowledgeBase ingest tamamlandı: {ChunkCount} chunk", docs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KnowledgeBase ingest sırasında hata.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ─── helpers ───

    /// <summary>Basit chunking — paragraf-aware, overlap'li.</summary>
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

    private string ComputeDirectoryHash()
    {
        if (!Directory.Exists(_rootDir)) return "empty";
        var sb = new StringBuilder();
        sb.Append("embed=").Append(_options.Embedding.Model)
          .Append(":").Append(_options.Embedding.Dimension).Append(';');
        foreach (var f in Directory.EnumerateFiles(_rootDir, "*.md", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var info = new FileInfo(f);
            sb.Append(f).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private string? ReadStateHash() => File.Exists(_stateFile) ? File.ReadAllText(_stateFile).Trim() : null;
}
