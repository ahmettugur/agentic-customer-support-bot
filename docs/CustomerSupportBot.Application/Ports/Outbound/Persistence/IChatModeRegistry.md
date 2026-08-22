# IChatModeRegistry

**Kaynak:** `Ports/Outbound/Persistence/IChatModeRegistry.cs`
**İmplementasyonlar:** [`InMemoryChatModeRegistry`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryChatModeRegistry.md), [`PostgresChatModeRegistry`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresChatModeRegistry.md)

## 1. Ne İşe Yarar

Session başına chat modunu (Bot / İnsan devraldı) tutan secondary port.

## 2. Hangi Amaçla Kullanılır

Admin bir sohbeti devraldığında (`TakeOver`) o oturumdaki gelen mesajlar artık bota değil
admin'e yönlendirilir; `Release` bot moduna geri döner. `ChatPortService` her turda
`GetMode` ile hangi modda olduğunu kontrol eder.

## 3. Sorumlulukları

- **Üstlendiği:** Mod durumunun (kim devraldı, ne zaman) tutulması ve sorgulanması.
- **Üstlenmediği:** Mesajların taşınması — o [`IChatBridge`](IChatBridge.md)'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`PostgresChatModeRegistry`'nin `TakeOver`/`Release` metotları DB-authoritative'dir (Redis
cache'e değil, doğrudan DB'ye yazıp okur) — iki adminin aynı oturumu eşzamanlı devralma
yarışını (race) önlemek için.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`TakeOver`/`Release` `bool` döner (exception değil) çünkü "zaten devralınmış" veya "zaten
serbest" beklenen, sık karşılaşılan durumlardır — çağıran taraf (admin panel endpoint'i) bunu
kullanıcıya "bu oturum zaten X tarafından devralınmış" gibi anlamlı bir mesajla iletir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ChatMode GetMode(string sessionId)` | Oturumun mevcut modu (Bot/Human). |
| `ChatSessionState? GetState(string sessionId)` | Oturumun tam durumu (kim devraldı, ne zaman). |
| `bool TakeOver(string sessionId, string? humanAgent)` | Oturumu devralır. Zaten devralınmışsa `false`. |
| `bool Release(string sessionId)` | Devri bırakır, bot moduna döner. |
| `IReadOnlyList<ChatSessionState> GetActive()` | Şu an insan tarafından yönetilen tüm oturumlar. |
| `event EventHandler<ChatSessionState>? ModeChanged` | Mod değiştiğinde fırlar. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ChatMode`/`ChatSessionState`'e bağımlıdır.
