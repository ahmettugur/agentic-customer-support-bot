using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Driving;

public sealed record MemoryConfig(
    string EmbeddingModel,
    int Dimension,
    int TopK,
    double MinScore);

/// <summary>
/// Semantic memory dashboard ve yönetim işlemleri için primary (driving) port.
/// Disabled durumda Enabled = false döner; diğer metotlar no-op sonuç verir.
/// </summary>
public interface IMemoryPort
{
    bool Enabled { get; }
    MemoryConfig Config { get; }
    Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default);
    Task IngestAsync(CancellationToken ct = default);
}
