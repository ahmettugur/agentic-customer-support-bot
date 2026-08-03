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
}
