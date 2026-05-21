namespace CustomerSupportBot.Application.Services.Memory;

/// <summary>
/// KnowledgeBase ingest use case'i için Application-internal servis arayüzü.
/// Implementasyon: Application/Services/Memory/KnowledgeBaseIngestionService.
/// Tüketici: MemoryPortService (IMemoryPort impl).
///
/// NOT: Bu arayüz Application-internal bir soyutlamadır — hiçbir adapter bağımlılığı yoktur.
/// Port değildir; Services/ altında doğru konumdadır.
/// </summary>
public interface IKnowledgeBaseIngestor
{
    Task IngestAsync(CancellationToken ct = default);
}
