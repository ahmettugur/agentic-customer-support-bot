// Services/Memory/IVectorMemoryStore.cs
// Qdrant gibi vector veritabanlarına soyutlanmış erişim.

using CustomerSupportBot.Api.Models.Memory;

namespace CustomerSupportBot.Api.Services.Memory;

public interface IVectorMemoryStore
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
