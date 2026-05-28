using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Müşteri geri bildirimi (1-5 yıldız + yorum) için secondary port.
///</summary>
public interface IRatingStore
{
    /// <summary>Yeni bir değerlendirme kaydeder. Session başına tek rating.</summary>
    ConversationRating Submit(string sessionId, int stars, string? feedback);

    /// <summary>Belirtilen oturumun rating'ini döndürür. Yoksa null.</summary>
    ConversationRating? GetBySession(string sessionId);

    /// <summary>Tüm rating'leri döndürür (analytics için).</summary>
    IReadOnlyList<ConversationRating> GetAll();

    /// <summary>Son N rating'i döndürür.</summary>
    IReadOnlyList<ConversationRating> GetRecent(int count = 20);
}
