// Application/Services/Memory/ISemanticMemoryIngestor.cs
// Application-internal servis arayüzü — vector store'a toplu doküman yazma kapasitesini soyutlar.
// Implementasyonlar: SemanticMemoryService (aktif), DisabledSemanticMemoryIngestor (devre dışı null-object).
// Tüketici: KnowledgeBaseIngestionService (Application).
//
// NOT: Bu arayüz Application-internal bir soyutlamadır — hiçbir adapter bunu tüketmez.
// Port değildir; Services/ altında doğru konumdadır.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Memory;

public interface ISemanticMemoryIngestor
{
    bool Enabled { get; }
    bool IsConfigured { get; }
    Task EnsureCollectionsAsync(CancellationToken ct = default);
    Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default);
}
