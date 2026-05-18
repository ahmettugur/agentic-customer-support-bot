// Ports/Driving/IAnalyticsPort.cs
// PRIMARY PORT — Analitik ve değerlendirme verileri.
// AnalyticsEndpoints bu port üzerinden okunur.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Analitik verileri için primary port.
/// </summary>
public interface IAnalyticsPort
{
    /// <summary>Belirtilen sessionId için derecelendirme kaydeder.</summary>
    ConversationRating Rate(string sessionId, int stars, string? comment = null);

    /// <summary>Tüm derecelendirmeler.</summary>
    IReadOnlyList<ConversationRating> GetAllRatings();

    /// <summary>Özet istatistikler (ortalama puan, toplam sayı vb.).</summary>
    object GetSummary();
}
