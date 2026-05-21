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
}
