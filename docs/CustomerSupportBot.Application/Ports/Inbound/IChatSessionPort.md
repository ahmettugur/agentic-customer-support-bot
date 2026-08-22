# IChatSessionPort ve Sonuç Kayıtları

**Dosya:** `Ports/Inbound/IChatSessionPort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

Bu dosyada bir interface (`IChatSessionPort`) ve onun kullandığı 5 küçük sonuç kaydı (`ChatSessionSentimentSnapshot`, `ChatSessionTakeoverResult`, `ChatSessionReleaseResult`, `ChatSessionMessageResult`, `ChatSessionReplanResult`) birlikte tanımlıdır.

## 1. Ne işe yarar?

Admin/agent panelinin canlı oturum yönetimi için kullandığı primary port: aktif oturumları listelemek, bir oturumu insan temsilcinin devralması (takeover), devri bırakması (release), admin'in kullanıcıya doğrudan mesaj göndermesi ve "replan" (yeniden planlama) akışları.

## 2. Hangi amaçla kullanılır?

Api katmanındaki admin/agent chat-session endpoint'leri ve SSE/WebSocket köprüleri (`SubscribeToAdminAsync`/`SubscribeToUserAsync`) bu portu kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Canlı oturum listesi, geçmiş, duygu durumu, devralma/bırakma, admin mesajı gönderme, replan, oturum event aboneliği.
- **Üstlenmediği:** Botun kendi reasoning/tool akışı — bu `IChatPort`'un işidir. Bu port yalnızca **insan müdahalesi** akışlarını kapsar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Chat` altındaki servis(ler).
- `ChatBridge` altyapısını (Redis/InMemory pub-sub) `SubscribeToAdminAsync`/`SubscribeToUserAsync` üzerinden dolaylı olarak kullanır.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Her aksiyon (`TakeOver`, `Release`, `SendAdminMessageAsync`, `ReplanSessionAsync`, `ReplanEscalationAsync`) kendi özel sonuç kaydını (`ChatSessionXxxResult`) döner; hepsi ortak bir `Success => ErrorCode is null` computed property'sine sahiptir. Bu, generic bir `bool`/`string` yerine tipli, genişletilebilir bir hata sözleşmesi sağlar — yeni bir hata kodu eklemek imza değişikliği gerektirmez.

## 6. Tipler ve Üyeler

### `ChatSessionSentimentSnapshot(string Sentiment, double Score, int ConsecutiveNegative, IReadOnlyList<SentimentEntry> History)`
Bir oturumun anlık duygu durumu özeti — mevcut sentiment etiketi, skor, art arda kaç olumsuz tur olduğu ve geçmiş kayıtlar.

### `ChatSessionTakeoverResult(string SessionId, string HumanAgent, int EscalationsAcknowledged, string? ErrorCode, string? ErrorMessage)`
Bir insan temsilcinin oturumu devralma sonucu; `EscalationsAcknowledged` o an kaç açık eskalasyonun otomatik onaylandığını taşır.

### `ChatSessionReleaseResult(string SessionId, int EscalationsResolved, string? ErrorCode, string? ErrorMessage)`
Devrin bırakılması (bota geri dönüş) sonucu.

### `ChatSessionMessageResult(string SessionId, string? ErrorCode, string? ErrorMessage)`
Admin'in kullanıcıya gönderdiği mesajın sonucu.

### `ChatSessionReplanResult(string SessionId, string? EscalationId, string RequestedBy, int EscalationsResolved, bool ReleasedFromHuman, string? ErrorCode, string? ErrorMessage)`
Admin'in "bu oturumu yeniden planla" talebinin sonucu; `ReleasedFromHuman` true ise oturum aynı zamanda insan modundan bota geri alınmıştır.

### `IChatSessionPort` metotları

| Metot | Açıklama |
|---|---|
| `IReadOnlyList<ChatSessionState> GetActive()` | Aktif tüm oturumlar. |
| `ChatSessionState GetStateOrDefault(string sessionId)` | Tek oturumun durumu (yoksa varsayılan). |
| `IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50)` | Oturumun köprü mesaj geçmişi. |
| `Task<ChatSessionSentimentSnapshot?> GetSentimentAsync(string sessionId, CancellationToken ct = default)` | Duygu durumu anlık görüntüsü. |
| `void PublishSystemMessage(string sessionId, string text)` | Sisteme ait bir bilgi mesajı yayınlar. |
| `ChatSessionTakeoverResult TakeOver(string sessionId, string humanAgent, string? agentId = null)` | İnsan temsilci oturumu devralır. |
| `ChatSessionReleaseResult Release(string sessionId, string? agentId = null)` | Devir bırakılır, oturum bota döner. |
| `Task<ChatSessionMessageResult> SendAdminMessageAsync(string sessionId, string humanAgent, string text, CancellationToken ct = default)` | Admin kullanıcıya mesaj gönderir. |
| `Task<ChatSessionReplanResult> ReplanSessionAsync(string sessionId, string requestedBy, string? note, CancellationToken ct = default)` | Oturum bazlı replan talebi. |
| `Task<ChatSessionReplanResult> ReplanEscalationAsync(string escalationId, string requestedBy, string? note, CancellationToken ct = default)` | Eskalasyon bazlı replan talebi. |
| `IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct)` | Admin tarafının canlı mesaj akışına abone olur. |
| `IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct)` | Kullanıcı tarafının canlı mesaj akışına abone olur. |
| `IReadOnlyList<EscalationRequest> GetOpenEscalations()` | Açık eskalasyonlar. |
| `int DismissOrphanedEscalations(string sessionId)` | Sahipsiz kalmış eskalasyonları temizler; kaç tanesinin temizlendiğini döner. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model` (`ChatSessionState`, `ChatBridgeMessage`, `SentimentEntry`, `EscalationRequest`).

## Bağlantılar

- [IHitlEventPort](IHitlEventPort.md) — event aboneliğinin başka bir görünümü.
