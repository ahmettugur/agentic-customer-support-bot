// Infrastructure/Persistence/Entities/Improvement/LessonEntity.cs
// `improvement.lessons` tablosu.
// SourceTraceIds listesi JSONB olarak saklanır.

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Improvement;

public sealed class LessonEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string LessonText { get; set; } = "";
    public string Observation { get; set; } = "";
    public string? SuggestedAgent { get; set; }
    public string SourceTraceIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "Proposed";
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? VectorMemoryId { get; set; }
}
