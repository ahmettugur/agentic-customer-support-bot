// Ports/Driven/AI/IVectorMemoryPort.cs
// SECONDARY PORT — Qdrant gibi vector veritabanlarına soyutlanmış erişim.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// Semantic memory için vektör deposu secondary port'u.
/// </summary>
public interface IVectorMemoryPort
{
    /// <summary>Koleksiyon yoksa yaratır (idempotent).</summary>
    Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default);

    /// <summary>Bir veya daha fazla dokümanı koleksiyona yazar (upsert).</summary>
    Task UpsertAsync(string collection,
        IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items,
        CancellationToken ct = default);

    /// <summary>Verilen vektöre en yakın N dokümanı döner.</summary>
    Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string collection,
        float[] query,
        int topK,
        float minScore,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default);

    /// <summary>Tek bir noktayı siler.</summary>
    Task DeleteAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>Koleksiyondaki nokta sayısı (dashboard için).</summary>
    Task<long> CountAsync(string collection, CancellationToken ct = default);
}
