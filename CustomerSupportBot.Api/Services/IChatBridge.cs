// Services/IChatBridge.cs
// HITL Live Takeover — user <-> admin arası mesaj köprüsü.
//
// Modeller:
//   - PublishUserMessage(): chat endpoint'ten admin tarafına push
//   - PublishAdminMessage(): admin endpoint'ten user tarafına push
//   - PublishSystemMessage(): "temsilci katıldı/ayrıldı" gibi nötr olaylar
//   - SubscribeToAdmin(): admin SSE — bu session'a gelen tüm user/system mesajları
//   - SubscribeToUser(): user SSE — bu session'a gelen tüm admin/system mesajları
//   - RecordBotExchange(): Bot moddayken her chat-stream sonunda çağrılır,
//     History buffer'ına yazılır → admin Üstlen'e tıkladığında bağlam görür
//   - GetHistory(): admin panel için son N mesaj (her sender dahil)

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

public interface IChatBridge
{
    void PublishUserMessage(string sessionId, string text);
    void PublishAdminMessage(string sessionId, string humanAgent, string text);
    void PublishSystemMessage(string sessionId, string text);

    /// <summary>
    /// Bot tarafından otomatik tetiklenmiş cevabı (ör. admin replan'ı sonrası
    /// arka planda üretilen yanıt) müşteriye normal bir bot mesajı gibi push'lar.
    /// Hem history'e yazılır hem persistent SSE'ye broadcast edilir.
    /// </summary>
    void PublishBotMessage(string sessionId, string text);

    /// <summary>
    /// Bot "yazıyor…" canlı göstergesi. on=true: typing indicator görünsün,
    /// on=false: kalkacak. History'e yazılmaz; transient bir kontrol mesajıdır.
    /// </summary>
    void PublishBotTyping(string sessionId, bool on);

    /// <summary>Bot moddayken (workflow akışı) çağrılır. History buffer'ına bot turunu yazar.</summary>
    void RecordBotExchange(string sessionId, string userQuery, string botResponse);

    /// <summary>
    /// Admin SSE: bu session'da admin tarafına push'lanan mesajları (User + System) yayınlar.
    /// Cancellation gelene kadar açık kalır.
    /// </summary>
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// User SSE: bu session'da user tarafına push'lanan mesajları (Admin + System) yayınlar.
    /// </summary>
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct);

    /// <summary>Admin panel için tüm geçmiş (Bot, User, Admin, System dahil).</summary>
    IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50);

    /// <summary>Session sonlandığında / temizlemek için.</summary>
    void Reset(string sessionId);
}
