// Adapters.Persistence/FileSystem/FileSystemKnowledgeBaseSource.cs
// IKnowledgeBaseSource driven port'unun dosya sistemi adaptörü.
// Directory tarama, SHA256 hash hesaplama ve state dosyası yönetimi burada gizlenir.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.FileSystem;

public sealed class FileSystemKnowledgeBaseSource : IKnowledgeBaseSource
{
    private readonly string _rootDir;
    private readonly string _stateFile;
    private readonly SemanticMemoryOptions _options;

    public FileSystemKnowledgeBaseSource(IOptions<SemanticMemoryOptions> options)
    {
        _options = options.Value;
        _rootDir  = Path.Combine(AppContext.BaseDirectory, "KnowledgeBase");
        _stateFile = Path.Combine(AppContext.BaseDirectory, ".kb-ingest-state.txt");
    }

    public bool Exists => Directory.Exists(_rootDir);

    public string ComputeDirectoryHash()
    {
        if (!Directory.Exists(_rootDir)) return "empty";

        var sb = new StringBuilder();
        // Embedding konfigürasyonu da hash'e dahil edilir — model değişince re-ingest tetiklenir.
        sb.Append("embed=").Append(_options.Embedding.Model)
          .Append(':').Append(_options.Embedding.Dimension).Append(';');

        foreach (var f in Directory.EnumerateFiles(_rootDir, "*.md", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var info = new FileInfo(f);
            sb.Append(f).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public string? ReadStateHash()
        => File.Exists(_stateFile) ? File.ReadAllText(_stateFile).Trim() : null;

    public void WriteStateHash(string hash)
        => File.WriteAllText(_stateFile, hash);

    public async IAsyncEnumerable<KnowledgeBaseFile> ReadFilesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(_rootDir, "*.md", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var content  = await File.ReadAllTextAsync(file, ct);
            var relative = Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
            var title    = Path.GetFileNameWithoutExtension(file);
            yield return new KnowledgeBaseFile(relative, title, content);
        }
    }
}
