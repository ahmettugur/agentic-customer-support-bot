namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// KnowledgeBase içeriğini vector store'a yükleyen driven port.
/// Implementasyon (dosya okuma + embedding) Adapters katmanındadır.
/// </summary>
public interface IKnowledgeBaseIngestor
{
    Task IngestAsync(CancellationToken ct = default);
}
