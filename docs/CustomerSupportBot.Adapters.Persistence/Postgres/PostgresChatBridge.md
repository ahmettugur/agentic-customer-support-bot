# PostgresChatBridge

**Dosya:** `Postgres/PostgresChatBridge.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IChatBridge`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md)

## 1. Ne İşe Yarar

"Canlı devralma" (live takeover) sırasında müşteri ↔ admin arasındaki mesaj köprüsü. `System.Threading.Channels.Channel<T>` tabanlı süreç-içi pub/sub'ı, kalıcı geçmiş (`chat.chat_bridge_messages`) ve Redis pub/sub ile birleştirir.

## 2. Hangi Amaçla Kullanılır

Bir admin bir sohbeti devraldığında ([`PostgresChatModeRegistry.TakeOver`](PostgresChatModeRegistry.md)), müşteri ve admin tarafındaki SSE/WebSocket bağlantıları bu sınıfın `SubscribeToUserAsync`/`SubscribeToAdminAsync` `IAsyncEnumerable`'larına abone olur; `PublishUserMessage`/`PublishAdminMessage` her iki tarafın gönderdiği mesajı ilgili abonelere dağıtır.

## 3. Sorumlulukları

- Üstlendiği: mesaj yayını (kim kime), kalıcı geçmiş yazımı (BotTyping hariç), abonelik yaşam döngüsü, cross-pod dağıtım.
- Üstlenmediği: modun (Bot/Human) kendisi (bkz. [`PostgresChatModeRegistry`](PostgresChatModeRegistry.md) — ayrı bir port/sınıf), mesaj içeriğinin üretimi (LLM/admin arayüzü).

## 4. İlişkiler

- `IChatBridge` portunu implemente eder.
- `IMessageBusPort` (Redis `csbot:bridge:touser`/`csbot:bridge:toadmin`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- [`PostgresChatModeRegistry`](PostgresChatModeRegistry.md) ile birlikte HITL canlı devralma özelliğinin iki yarısını oluşturur.

## 5. Tasarım Yaklaşımı

> 🐞 **`ConcurrentBag` → `ConcurrentDictionary<Channel, byte>` geçişi:** Abonelik kaydı eskiden `ConcurrentBag<Channel<ChatBridgeMessage>>` idi ve bir WebSocket/SSE bağlantısı kapandığında kanal **hiçbir zaman** kayıttan çıkarılmıyordu — `ConcurrentBag` zaten tekil eleman silmeyi desteklemiyor. Sık bağlanıp kopan bir oturumda (sayfa yenileme, WebSocket yeniden bağlanma) tamamlanmış-ama-hâlâ-tutulan kanallar process ömrü boyunca birikiyordu: bellek sınırsız büyüyor ve her `Broadcast` çağrısı artık kimsenin okumadığı bu kanalları da tarayarak session'ın yaşı ilerledikçe yavaşlıyordu. Çözüm: kayıt yapısı, `Channel`'ı anahtar olarak kullanan (kümeye eşdeğer) bir `ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>`'a çevrildi ve `Unregister`, abonelik `finally` bloğunda çağrılıyor.

> 🐞 **`Reset` neden DB kaydını silmiyor:** Yalnızca in-memory state (history buffer + abonelikler) temizlenir; DB'deki mesajlar audit amacıyla kalıcı olarak saklanır.

`BotTyping` mesajı (`PublishBotTyping`) bilinçli olarak `Append`'e (dolayısıyla DB'ye) hiç gönderilmez — geçici bir UI sinyalidir, kalıcı geçmiğin parçası değildir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task PublishUserMessageAsync(...)` / `void PublishAdminMessage/PublishSystemMessage/PublishAdminOnlyMessage/PublishBotMessage(...)` | Gönderen/hedef kombinasyonuna göre `Append` (kalıcılık) + `Broadcast` (canlı dağıtım) + Redis yayını yapan varyantlar. `PublishAdminOnlyMessage` müşteriye gitmez (örn. sistem notu). |
| `void PublishBotTyping(string sessionId, bool on)` | Geçici "yazıyor…" göstergesi — kalıcı değildir. |
| `Task RecordBotExchangeAsync(string sessionId, string userQuery, string botResponse)` | Bot modunda geçen bir turu (canlı yayın yapmadan) yalnızca geçmişe ekler — admin devraldığında konuşmanın öncesini görebilsin diye. |

> **Async durumu:** Tüm yazma yolları (`Publish*Async`, `RecordBotExchangeAsync`) ve geçmiş okuması (`GetHistoryAsync`) gerçek async'tir — adaptörde hiç `GetAwaiter().GetResult()` kalmadı. Oturum başına hydrate `Lazy<Task>` ile yapılır: eşzamanlı çağıranlar aynı yüklemeyi bekler (eskiden bayrak DB okumasından önce konduğu için ikinci çağıran boş geçmiş görüyor ve mesajı iki kez yazılabiliyordu). `GetHistoryAsync` listeyi kilit altında kopyalar.
| `IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync/SubscribeToUserAsync(string sessionId, CancellationToken ct)` | Yeni bir `Channel` oluşturup kaydeder, tüketici döngüsü bitince (bağlantı kapanınca) `Unregister` ile temizler. |
| `Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50)` | Bellek halkasından (veya gerekirse DB'den hydrate ederek) son N mesaj. |
| `void Reset(string sessionId)` | Bellek state'ini temizler, tüm açık kanalları `TryComplete` ile kapatır; DB'ye dokunmaz. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresChatBridge>`

## Bağlantılar

- [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md)
- [PostgresChatModeRegistry](PostgresChatModeRegistry.md) — mod yönetimi (bu sınıfın tamamlayıcısı)
