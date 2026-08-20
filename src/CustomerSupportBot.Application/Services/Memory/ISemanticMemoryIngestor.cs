// Application/Services/Memory/ISemanticMemoryIngestor.cs
// Application-internal servis arayüzü — vector store'a toplu doküman yazma kapasitesini soyutlar.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Memory;

public interface ISemanticMemoryIngestor
{
    bool Enabled { get; }
    bool IsConfigured { get; }
    Task EnsureCollectionsAsync(CancellationToken ct = default);
    Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default);

    /// <summary>Tek dokümanı indeksten siler. Kayıt yoksa sessizce geçer (idempotent).</summary>
    Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default);

    /// <summary>
    /// Verilen etiketi taşımayan (ya da farklı değer taşıyan) tüm belgeleri siler.
    /// Yeniden indekslemeden sonra ARTIK ÜRETİLMEYEN belgeleri temizlemek için — bkz.
    /// <see cref="Ports.Outbound.AI.IVectorMemoryPort.DeleteWhereTagNotAsync"/>.
    /// </summary>
    Task DeleteStaleAsync(MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default);

    /// <summary>
    /// Koleksiyondaki kayıt sayısı. Ingestion'ın "kaynak değişmedi" kısayolu, koleksiyonun
    /// gerçekten dolu olduğunu da doğrulamak zorundadır — koleksiyon dışarıdan yeniden
    /// oluşturulduğunda kaynak hash'i aynı kalır ve re-ingest sessizce atlanırdı.
    /// </summary>
    Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default);
}
