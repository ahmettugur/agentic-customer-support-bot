// Ports/Driving/ISemanticMemoryIngestor.cs
// PRIMARY PORT — KnowledgeBaseIngestor (hosting adapter) core'u bu arayüz üzerinden sürer.
// Implementasyon: Application/Services/Memory/SemanticMemoryService (hexagonal: core'da).

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Driving;

public interface ISemanticMemoryIngestor
{
    bool Enabled { get; }
    bool IsConfigured { get; }
    Task EnsureCollectionsAsync(CancellationToken ct = default);
    Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default);
}
