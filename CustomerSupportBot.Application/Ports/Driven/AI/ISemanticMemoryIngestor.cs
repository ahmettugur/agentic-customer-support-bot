// Ports/Driven/AI/ISemanticMemoryIngestor.cs
// SECONDARY PORT — KnowledgeBaseIngestor'ın SemanticMemoryService'e erişim sözleşmesi.
// Yalnızca KB ingestion sürecinin ihtiyaç duyduğu üyeleri açığa çıkarır.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Driven.AI;

public interface ISemanticMemoryIngestor
{
    bool Enabled { get; }
    bool IsConfigured { get; }
    Task EnsureCollectionsAsync(CancellationToken ct = default);
    Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default);
}
