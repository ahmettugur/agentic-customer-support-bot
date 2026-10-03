# IChatBridge

**Kaynak:** `Ports/Outbound/Persistence/IChatBridge.cs`
**İmplementasyonlar:** [`InMemoryChatBridge`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryChatBridge.md), [`PostgresChatBridge`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresChatBridge.md)

## 1. Ne İşe Yarar

HITL Live Takeover özelliği için User ↔ Admin arası gerçek zamanlı mesaj köprüsü. Bir admin bir
oturumu devraldığında (bkz. [`IChatModeRegistry`](IChatModeRegistry.md)) kullanıcı ile admin
arasındaki mesajlar bu port üzerinden akar.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki canlı sohbet ekranı `SubscribeToAdminAsync`, kullanıcı tarafındaki chat SSE
bağlantısı `SubscribeToUserAsync` ile bu köprüye abone olur; `PublishXxxMessage` metotları
mesaj yayınlar.

## 3. Sorumlulukları

- **Üstlendiği:** Kullanıcı/admin/bot/sistem mesajlarının yayınlanması, aboneliklerin
  yönetimi, oturum bazlı mesaj geçmişi.
- **Üstlenmediği:** Mod geçişi (kim devraldı, kim bıraktı) — o
  [`IChatModeRegistry`](IChatModeRegistry.md)'nin işi; bu port yalnızca mesaj akışını taşır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `InMemoryChatBridge` (tek pod) ve `PostgresChatBridge` (çoklu pod, Redis pub/sub ile senkron)
  implemente eder. İkisi de aboneliklerini `ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>`
  ile tutar — `ConcurrentBag<T>` tekil eleman silmeyi desteklemediği için bu desen tercih
  edilmiştir (abonelik iptalinde gerçek kaldırma gerekir, sızıntı yaratmaz).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`PublishAdminOnlyMessage`, `PublishAdminMessage`'dan ayrı bir metot olarak var: bazı sistem
notları (örn. "müşteri bu ürünü daha önce iade etmiş") yalnızca admin'in görmesi gerekir,
history'ye yazılır ama müşteri kanalına gönderilmez — bu ayrım metot seviyesinde net tutulur,
çağıranın yanlışlıkla müşteriye sızdırma riski kalmaz.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task PublishUserMessageAsync(string sessionId, string text)` | Kullanıcıdan gelen mesajı yayınlar (her insan-modu mesajında çalışır — async). |
| `Task PublishAdminMessageAsync(string sessionId, string humanAgent, string text)` | Admin'den gelen mesajı yayınlar (her iki kanala da). |
| `Task PublishSystemMessageAsync(string sessionId, string text)` | Sistem bildirimini yayınlar. |
| `Task PublishAdminOnlyMessageAsync(string sessionId, string text)` | History'ye yazar, yalnızca admin kanalına gönderir — müşteri görmez. |
| `Task PublishBotMessageAsync(string sessionId, string text)` | Bot cevabını yayınlar. |
| `void PublishBotTyping(string sessionId, bool on)` | "Bot yazıyor" göstergesi. |
| `Task RecordBotExchangeAsync(string sessionId, string userQuery, string botResponse)` | Bir bot turunu geçmişe kaydeder (her turda çalışır — async). |
| `IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct)` | Admin kanalına abone olur. |
| `IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct)` | Kullanıcı kanalına abone olur. |
| `Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50)` | Oturum mesaj geçmişi. |
| `void Reset(string sessionId)` | Oturumun köprü durumunu temizler. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ChatBridgeMessage`'a bağımlıdır.
