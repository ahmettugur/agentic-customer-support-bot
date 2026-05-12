// Services/IRatingStore.cs
// Konuşma değerlendirme deposu arayüzü.
// Persistence geçişinde sadece bu arayüzün yeni implementasyonu yazılacak.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

/// <summary>
/// Müşteri geri bildirimi (1-5 yıldız + yorum) depolama arayüzü.
/// </summary>
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
