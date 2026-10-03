using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// HITL Live Takeover için secondary port — User ↔ Admin arası mesaj köprüsü.
///</summary>
public interface IChatBridge
{
    /// <summary>
    /// İnsan modundaki müşteri mesajını temsilciye iletir ve kalıcılaştırır. Her müşteri
    /// mesajında çalışır — bu yüzden asenkron (DB beklenirken thread rehin tutulmaz).
    /// </summary>
    Task PublishUserMessageAsync(string sessionId, string text);
    Task PublishAdminMessageAsync(string sessionId, string humanAgent, string text);
    Task PublishSystemMessageAsync(string sessionId, string text);
    /// <summary>History'ye yazar, sadece admin kanalına gönderir — müşteri görmez.</summary>
    Task PublishAdminOnlyMessageAsync(string sessionId, string text);
    Task PublishBotMessageAsync(string sessionId, string text);
    void PublishBotTyping(string sessionId, bool on);
    /// <summary>
    /// Bot turunu (kullanıcı + bot mesajı) admin paneli geçmişine yazar. Her bot turunun
    /// sonunda çalışır — bu yüzden asenkron.
    /// </summary>
    Task RecordBotExchangeAsync(string sessionId, string userQuery, string botResponse);

    IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct);
    /// <summary>
    /// Son <paramref name="take"/> mesaj. Oturumun ilk okuması geçmişi DB'den yükler (oturum
    /// başına hydrate açılışta ısıtılamaz) — bu yüzden asenkron.
    /// </summary>
    Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50);
    void Reset(string sessionId);
}
