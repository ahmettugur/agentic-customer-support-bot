// Infrastructure/Persistence/Entities/Knowledge/KnowledgeArticleEntity.cs
// `knowledge.articles` tablosu — panelden yönetilen bilgi tabanı makaleleri.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Knowledge;

public sealed class KnowledgeArticleEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Category { get; set; }
    public bool IsPublished { get; set; }
    public int IndexedChunkCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
