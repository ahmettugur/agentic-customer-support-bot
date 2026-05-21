namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// KnowledgeBase ingest use case'i için Application-internal servis arayüzü.
/// Implementasyon: Application/Services/Memory/KnowledgeBaseIngestionService.
/// Tüketici: MemoryPortService (IMemoryPort impl).
/// </summary>
public interface IKnowledgeBaseIngestor
{
    Task IngestAsync(CancellationToken ct = default);
}
