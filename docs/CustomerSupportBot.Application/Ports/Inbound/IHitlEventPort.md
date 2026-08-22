# IHitlEventPort ve IHitlEventSubscription

**Dosya:** `Ports/Inbound/IHitlEventPort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Chat streaming adaptörünün (SSE/WebSocket) bir oturuma özel HITL (onay/eskalasyon) ve genel chat event'lerini dinlemek için kullandığı primary port.

## 2. Hangi amaçla kullanılır?

`/chat/stream` gibi SSE bağlantıları ve kalıcı `/chat/events/{sessionId}` bağlantısı, bu porttan bir abonelik alıp gelen event'leri (`Func<string, object, Task> onEvent` callback'i ile) doğrudan istemciye iletir.

## 3. Sorumlulukları

- **Üstlendiği:** Oturum bazlı event aboneliği açmak ve `IDisposable` ile temiz kapatılmasını sağlamak.
- **Üstlenmediği:** Event'lerin nasıl üretildiği (`IApprovalPort`, `IEscalationPort` gibi diğer portların event'lerinden türetilir) — bu port sadece dinleme arayüzüdür.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `HitlEventPortService` (Application/Services/Escalation).
- Api katmanındaki chat SSE endpoint'leri tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

İki ayrı `Subscribe` metodu vardır çünkü iki farklı bağlantı türü farklı event kümelerine ilgi duyar: `Subscribe` geçici `/chat/stream` bağlantısı için (approval/escalation event'leri), `SubscribeToChatEvents` ise kalıcı `/chat/events/{sessionId}` bağlantısı için (daha geniş bir event kümesi, ör. `approval_resolved`). `IHitlEventSubscription : IDisposable` deseni, abonelik sonlandığında (bağlantı kapandığında) event handler'ın registry'den düzgün çıkarılmasını garanti eder — sızıntı önler.

## 6. Metotlar / Üyeler

### `IHitlEventSubscription : IDisposable`
Markır arayüz — bir aboneliği temsil eder, `Dispose()` çağrıldığında abonelik iptal edilir.

### `IHitlEventPort`

| Metot | Açıklama |
|---|---|
| `IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent)` | Geçici stream bağlantısı için approval/escalation event'lerine abone olur. |
| `IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent)` | Kalıcı chat-events bağlantısı için daha geniş event kümesine abone olur. |

## 7. Bağımlılıklar

Yok (arayüz düzeyinde) — implementasyon `IApprovalPort`/`IEscalationPort` event'lerine bağımlıdır.

## Bağlantılar

- [IApprovalPort](IApprovalPort.md), [IEscalationPort](IEscalationPort.md) — event kaynakları.
- [IChatSessionPort](IChatSessionPort.md) — `SubscribeToAdminAsync`/`SubscribeToUserAsync` ile benzer amaçlı, farklı bir kanal.
