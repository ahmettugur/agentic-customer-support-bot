namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// KnowledgeBase içeriğini vector store'a yükleyen driven port.
/// Implementasyon: Api/Workers/KnowledgeBaseIngestor.cs (hosting adapter).
/// </summary>
public interface IKnowledgeBaseIngestor
{
    Task IngestAsync(CancellationToken ct = default);
}
