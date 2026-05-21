namespace CustomerSupportBot.Application.Ports.Driven.AI;

public sealed record KnowledgeBaseFile(string RelativePath, string Title, string Content);

/// <summary>
/// KnowledgeBase dosya kaynağı için secondary (driven) port.
/// Filesystem erişimi (Directory, File, SHA) adaptör tarafında gizlenir.
/// </summary>
public interface IKnowledgeBaseSource
{
    bool Exists { get; }

    /// <summary>Dizin dosya metadata'sı + embedding konfigürasyonundan SHA256 parmakizi üretir.</summary>
    string ComputeDirectoryHash();

    string? ReadStateHash();
    void WriteStateHash(string hash);

    IAsyncEnumerable<KnowledgeBaseFile> ReadFilesAsync(CancellationToken ct);
}
