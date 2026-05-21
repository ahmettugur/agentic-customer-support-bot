namespace CustomerSupportBot.Application.Services.Memory;

/// <summary>
/// KnowledgeBase ingest use case'i için Application-internal servis arayüzü.
/// </summary>
public interface IKnowledgeBaseIngestor
{
    Task IngestAsync(CancellationToken ct = default);
}
