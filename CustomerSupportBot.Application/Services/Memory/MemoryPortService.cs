// Application/Services/MemoryPortService.cs
// DRIVING PORT IMPL — IMemoryPort → SemanticMemoryService + IKnowledgeBaseIngestor.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed class MemoryPortService : IMemoryPort
{
    private readonly SemanticMemoryService _memory;
    private readonly IKnowledgeBaseIngestor _ingestor;

    public MemoryPortService(SemanticMemoryService memory, IKnowledgeBaseIngestor ingestor)
    {
        _memory = memory;
        _ingestor = ingestor;
    }

    public bool Enabled => _memory.Enabled;

    public MemoryConfig Config => new(
        _memory.Options.Embedding.Model,
        _memory.Options.Embedding.Dimension,
        _memory.Options.Retrieval.TopK,
        _memory.Options.Retrieval.MinScore);

    public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
        => _memory.CountAsync(kind, ct);

    public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default)
        => _memory.SearchAsync(kind, query, topK: topK, ct: ct);

    public Task IngestAsync(CancellationToken ct = default)
        => _ingestor.IngestAsync(ct);
}

/// <summary>
/// SemanticMemory devre dışıyken kullanılan no-op implementasyon.
/// </summary>
public sealed class DisabledMemoryPort : IMemoryPort
{
    public bool Enabled => false;
    public MemoryConfig Config => new("", 0, 0, 0.0);
    public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default) => Task.FromResult(0L);
    public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemorySearchHit>>(Array.Empty<MemorySearchHit>());
    public Task IngestAsync(CancellationToken ct = default) => Task.CompletedTask;
}
