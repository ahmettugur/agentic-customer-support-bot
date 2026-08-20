using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

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

    /// <summary>
    /// Belirtilen etiketin verilen değere <b>eşit OLMADIĞI</b> tüm noktaları siler.
    ///
    /// <para>
    /// Yeniden indeksleme için: her ingest turu ürettiği belgeleri kendi damgasıyla yazar,
    /// sonra bu damgayı taşımayanları siler. Tek tek id silmekle yapılamaz, çünkü silinmesi
    /// gerekenler tam olarak <b>artık üretilmeyen</b> belgelerdir — kaynak dosyası silinmiş
    /// ya da küçülmüş olanlar. Onların id'leri yeni turda hiç görünmez, dolayısıyla
    /// bilinemezler; tanımlanabilecekleri tek şey damgalarının eskiliğidir.
    /// </para>
    /// </summary>
    Task DeleteWhereTagNotAsync(
        string collection, string tagKey, string tagValue, CancellationToken ct = default);

    /// <summary>Koleksiyondaki nokta sayısı (dashboard için).</summary>
    Task<long> CountAsync(string collection, CancellationToken ct = default);
}
