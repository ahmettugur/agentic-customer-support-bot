// Services/ISessionManager.cs
// Oturum yönetim arayüzü — IConversationStore'u genişletir.
// Mesaj geçmişi + oturum durumu (state) birlikte yönetilir.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

/// <summary>
/// IConversationStore'u genişleten oturum yöneticisi.
/// Mesaj geçmişi + durum (state) + yaşam döngüsü yönetimi sağlar.
/// </summary>
public interface ISessionManager : IConversationStore
{
    /// <summary>
    /// Mevcut oturumu getirir veya yeni oluşturur.
    /// SessionId null ise yeni GUID ile oluşturulur.
    /// </summary>
    AgentSession GetOrCreateSession(string? sessionId);

    /// <summary>
    /// Belirtilen oturumu döndürür. Bulunamazsa null döner.
    /// </summary>
    AgentSession? GetSession(string sessionId);

    /// <summary>
    /// Oturum durumunu günceller (state değişiklikleri).
    /// </summary>
    void UpdateSession(AgentSession session);

    /// <summary>
    /// Konuşma içeriğinden durum bilgilerini çıkarır ve session state'i günceller.
    /// Ör: Müşteri kimlik numarası, niyet, sipariş numarası vb.
    /// </summary>
    void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse);
}
