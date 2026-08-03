namespace CustomerSupportBot.Web.Models;

public sealed class KnowledgeArticleDto
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

    public KnowledgeArticleDto Clone() => new()
    {
        Id = Id,
        Title = Title,
        Content = Content,
        Category = Category,
        IsPublished = IsPublished,
        IndexedChunkCount = IndexedChunkCount,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };
}

public sealed class KnowledgeArticleInput
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Category { get; set; }
    public bool IsPublished { get; set; }
}

/// <summary>Kaydetme yanıtı — indeksleme ayrı başarısız olabildiği için uyarı taşır.</summary>
public sealed class KnowledgeArticleSaveDto
{
    public KnowledgeArticleDto? Article { get; set; }
    public bool Indexed { get; set; }
    public string? Warning { get; set; }
}
