// Infrastructure/Persistence/Entities/Analytics/RatingEntity.cs
// `analytics.ratings` tablosu — Konuşma değerlendirmeleri.
// Session başına tek rating (PK = session_id) — domain Id audit için ayrıca tutulur.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;

public sealed class RatingEntity
{
    public string SessionId { get; set; } = "";
    public string Id { get; set; } = "";
    public int Stars { get; set; }
    public string? Feedback { get; set; }
    public DateTime RatedAt { get; set; }
}
