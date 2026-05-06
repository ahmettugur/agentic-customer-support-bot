// Models/Improvement/Lesson.cs
// Self-improving loop tarafından üretilen "öğrenilmiş ders".
// LessonMiner düşük-puanlı veya hatalı trace'leri analiz edip Lesson önerir.
// Admin Approve/Reject ile karara bağlar; Approved olanlar Qdrant Lessons collection'ına yazılır
// ve sonraki konuşmalarda SemanticMemoryContextProvider üzerinden context'e dönerler.

namespace CustomerSupportBot.Models.Improvement;

public enum LessonStatus
{
    Proposed,
    Approved,
    Rejected
}

public sealed class Lesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Kısa başlık — admin paneli listesinde görünür.</summary>
    public string Title { get; set; } = "";

    /// <summary>"X durumunda Y yapılmalı" şeklinde uygulanabilir kural.</summary>
    public string LessonText { get; set; } = "";

    /// <summary>Hangi sorunun bu ders ile çözüldüğüne dair gözlem.</summary>
    public string Observation { get; set; } = "";

    /// <summary>Opsiyonel — hangi prompt/agent bu dersi uygulamalı önerisi.</summary>
    public string? SuggestedAgent { get; set; }

    /// <summary>Bu lesson'ı doğuran trace'ler (diagnoz için).</summary>
    public List<string> SourceTraceIds { get; set; } = new();

    public LessonStatus Status { get; set; } = LessonStatus.Proposed;

    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Approve sonrası Qdrant'a upsert edildiğinde set edilir.</summary>
    public string? VectorMemoryId { get; set; }
}
