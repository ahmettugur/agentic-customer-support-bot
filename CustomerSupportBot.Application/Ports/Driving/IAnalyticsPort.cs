using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Analitik verileri için primary port.
/// </summary>
public interface IAnalyticsPort
{
    /// <summary>Belirtilen sessionId için derecelendirme kaydeder.</summary>
    ConversationRating Rate(string sessionId, int stars, string? comment = null);

    /// <summary>Tek bir session için derecelendirmeyi döner.</summary>
    ConversationRating? GetRating(string sessionId);

    /// <summary>Son N derecelendirmeyi döner.</summary>
    IReadOnlyList<ConversationRating> GetRecentRatings(int count = 20);

    /// <summary>Tüm derecelendirmeler.</summary>
    IReadOnlyList<ConversationRating> GetAllRatings();

    /// <summary>Özet istatistikler (ortalama puan, toplam sayı vb.).</summary>
    object GetSummary();

    /// <summary>Tüm dashboard istatistiklerini döner.</summary>
    AnalyticsDashboard GetDashboard();

    /// <summary>Tek bir oturum için detaylı analytics döner.</summary>
    SessionAnalytics? GetSessionAnalytics(string sessionId);
}
