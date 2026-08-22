# InMemoryChatBridge

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryChatBridge.cs`
- **Port:** `IChatBridge`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

HITL "Live Takeover" (admin devralması) sırasında müşteri ↔ admin arasındaki canlı mesaj akışını `System.Threading.Channels.Channel<T>` tabanlı bir pub/sub ile taşıyan bellek içi köprüdür. Ayrıca son 200 mesajı (`_history`) ring buffer olarak tutar — admin devraldığı anda bağlamı okuyabilsin diye.

## 2. Hangi Amaçla Kullanıldığı

`PostgresChatBridge`'in (production, Redis pub/sub destekli) tek-process karşılığıdır. Kullanıcı mesajları, admin mesajları, sistem mesajları, bot mesajları ve "bot yazıyor" sinyalleri bu sınıf üzerinden ilgili abonelere (SSE/WebSocket bağlantıları) dağıtılır.

## 3. Sorumlulukları

- Her session için, her admin/kullanıcı aboneliğine ayrı bir `Channel<ChatBridgeMessage>` açma (`SubscribeToAdminAsync`/`SubscribeToUserAsync`) — aynı session'a birden fazla admin veya sayfa yenilemesi sonrası tekrar bağlanan kullanıcı olabilir.
- Mesaj yayınlama yönü mesajın türüne göre değişir: kullanıcı mesajı → admin'e, admin mesajı → kullanıcıya, sistem mesajı → her ikisine, "admin-only" mesajı (ör. sistem uyarısı) → sadece admin'e, bot mesajı → her ikisine.
- `RecordBotExchange` — bot moddaki (admin devretmemiş) konuşmaları sadece geçmişe yazar, broadcast ETMEZ (kullanıcı zaten kendi ekranında görüyor); admin bağlam için `GetHistory` ile çeker.
- Abonelik sonlandığında (`finally` bloğu) kaydı **gerçekten** kayıttan çıkarma (`Unregister`) — aksi halde artık dinlemeyen ölü kanallar biriktirir (bellek sızıntısı).
- `Reset(sessionId)` — session kapandığında geçmişi siler ve tüm açık kanalları `TryComplete()` ile kapatır (abonelerin `await foreach` döngüsü düzgün sonlanır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresChatBridge` (`../Postgres/HitlAndChat.md`) ile birebir aynı `IChatBridge` arayüzünü uygular ve **aynı `Unregister` mantığını** paylaşır (kaynak kod içi yorum bu paralelliğe açıkça atıf yapar).
- `IChatModeRegistry`'nin (`InMemoryChatModeRegistry`) yönettiği bot/human mod bilgisiyle birlikte çalışır — hangi mesajın hangi yöne broadcast edileceğine karar veren mod, ayrı bir bileşendedir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>` kullanımı (bir `ConcurrentBag` değil) **bilinçli bir tasarım kararıdır**: `ConcurrentBag<T>` tekil eleman silmeyi desteklemez, oysa bir abone bağlantısını kapattığında (`finally` bloğunda) SADECE kendi kanalını kayıttan çıkarmak gerekir — bu yüzden "silinebilir küme" olarak `ConcurrentDictionary` (değer alanı kullanılmaz, sadece anahtar kümesi) tercih edilmiştir.

> 🐞 **Geçmiş regresyon:** Bu sınıfın ilk hâli `ConcurrentBag<Channel<...>>` kullanıyordu; abonelik kapandığında kanal kayıttan çıkarılamıyordu, bu da uzun ömürlü session'larda birikip duran "ölü" kanallara ve `Broadcast`'in onlara boşuna yazmaya çalışmasına yol açıyordu. `ChatBridgeSubscriptionCleanupTests` bu davranışı doğrular.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `PublishUserMessage(sessionId, text)` | Kullanıcı mesajını geçmişe ekler ve admin'e broadcast eder. |
| `PublishAdminMessage(sessionId, humanAgent, text)` | Admin mesajını geçmişe ekler ve kullanıcıya broadcast eder. |
| `PublishSystemMessage(sessionId, text)` | Her iki tarafa da giden sistem mesajı. |
| `PublishAdminOnlyMessage(sessionId, text)` | Sadece admin'e giden sistem mesajı (müşteri görmez). |
| `PublishBotMessage(sessionId, text)` | Bot yanıtını hem kullanıcıya hem admin paneline (bağlam takibi için) yayınlar. |
| `PublishBotTyping(sessionId, on)` | Geçici "yazıyor" sinyali — geçmişe yazılmaz, sadece kullanıcıya gider. |
| `RecordBotExchange(sessionId, userQuery, botResponse)` | Bot modundaki değişimi sadece geçmişe kaydeder, broadcast etmez. |
| `SubscribeToAdminAsync(sessionId, ct)` / `SubscribeToUserAsync(sessionId, ct)` | Yeni bir `Channel` açar, kaydeder, `IAsyncEnumerable` olarak mesajları akıtır; `finally`'de kanalı tamamlar ve kayıttan çıkarır. |
| `GetHistory(sessionId, take)` | Son `take` mesajı döner. |
| `Reset(sessionId)` | Geçmişi siler, tüm açık kanalları kapatır. |

## 7. Bağımlılıklar

- `ILogger<InMemoryChatBridge>`
