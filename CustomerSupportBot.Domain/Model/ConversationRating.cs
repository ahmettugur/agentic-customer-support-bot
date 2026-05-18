// Models/ConversationRating.cs
// Konuşma sonu müşteri geri bildirimi modeli.
// Kullanıcı 1-5 yıldız + opsiyonel yorum bırakabilir.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir oturum için kullanıcının bıraktığı değerlendirme.
/// </summary>
public class ConversationRating
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SessionId { get; set; } = "";

    /// <summary>1–5 arası yıldız puanı.</summary>
    public int Stars { get; set; }

    /// <summary>Opsiyonel kullanıcı yorumu.</summary>
    public string? Feedback { get; set; }

    public DateTime RatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Rating submit endpoint input modeli.
/// </summary>
public record RatingInput(int Stars, string? Feedback);

/// <summary>
/// Analytics dashboard için özet veri modeli.
/// </summary>
public class AnalyticsDashboard
{
    // ─── Genel ───
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public double AverageSessionMessages { get; set; }

    // ─── Rating ───
    public double AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
    public List<ConversationRating> RecentRatings { get; set; } = new();

    // ─── Approvals ───
    public int TotalApprovals { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int PendingCount { get; set; }

    // ─── Escalations ───
    public int TotalEscalations { get; set; }
    public int OpenEscalations { get; set; }
    public int AcknowledgedEscalations { get; set; }
    public int ResolvedEscalations { get; set; }
    public int DismissedEscalations { get; set; }

    // ─── Intent dağılımı ───
    public Dictionary<string, int> IntentDistribution { get; set; } = new();

    // ─── Faz dağılımı ───
    public Dictionary<string, int> PhaseDistribution { get; set; } = new();

    // ─── Duygu analizi ───
    public double AverageSentimentScore { get; set; }
    public Dictionary<string, int> SentimentDistribution { get; set; } = new();
    public int NegativeSessionCount { get; set; }
    public int SentimentAlertCount { get; set; }
}

