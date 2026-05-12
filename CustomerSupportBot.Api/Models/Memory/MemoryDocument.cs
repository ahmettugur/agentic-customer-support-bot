// Models/Memory/MemoryDocument.cs
// Semantic memory'de saklanan tek bir dokümanı temsil eder.
// Üç tip kullanılıyor: Episodic (geçmiş konuşmalar), Lesson (kapalı-döngü iyileştirme),
// Knowledge (statik dokümantasyon — politika, SSS).

namespace CustomerSupportBot.Models.Memory;

/// <summary>Memory dokümanının türü — collection seçimi ve filtreleme için.</summary>
public enum MemoryKind
{
    /// <summary>Geçmiş konuşma turu (sorgu + yanıt + outcome).</summary>
    Episodic,
    /// <summary>Self-improvement loop tarafından üretilmiş ders (admin onaylı).</summary>
    Lesson,
    /// <summary>KnowledgeBase/ dizininden ingest edilmiş statik bilgi.</summary>
    Knowledge
}

/// <summary>Vector store'a yazılacak / okunacak doküman.</summary>
public sealed class MemoryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MemoryKind Kind { get; set; }
    public string Text { get; set; } = "";

    /// <summary>İnsanın okuyacağı kısa başlık (UI / citation için).</summary>
    public string? Title { get; set; }

    /// <summary>Kaynak (örn. dosya adı, traceId, lessonId).</summary>
    public string? Source { get; set; }

    /// <summary>Bağlı olduğu oturum (Episodic için zorunlu).</summary>
    public string? SessionId { get; set; }

    /// <summary>Ek metadata (intent, agent, rating gibi filtre alanları).</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Vector search sonucu (doküman + benzerlik skoru).</summary>
public sealed class MemorySearchHit
{
    public required MemoryDocument Document { get; init; }
    public required float Score { get; init; }
}
