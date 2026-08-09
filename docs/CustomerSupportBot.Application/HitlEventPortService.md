# HitlEventPortService

**Dosya:** `Services/Escalation/HitlEventPortService.cs`  
**Implements:** `IHitlEventPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Admin panel ve müşteri tarafı için HITL (Human-in-the-Loop) olay akışlarını sağlar. SSE (Server-Sent Events) üzerinden gerçek zamanlı bildirim gönderir. İki farklı abonelik türü sunar. API, `IAsyncEnumerable` tabanlı bir pull-model **değil** — **push/callback** tabanlıdır.

---

## Arayüz

```csharp
public interface IHitlEventSubscription : IDisposable;

public interface IHitlEventPort
{
    IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent);
    IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent);
}
```

`IHitlEventSubscription` sadece `IDisposable`'dır — `EventStream` gibi bir property yoktur, `HitlEvent` diye bir tip de yoktur. Her olay gerçekleştiğinde `onEvent(eventType, payload)` callback'i doğrudan çağrılır (fire-and-forget, `ContinueWith` ile).

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IApprovalQueue` | Onay event kaynağı |
| `IEscalationSink` | Eskalasyon event kaynağı |
| `IChatModeRegistry` | Bot↔Human mod değişikliği event kaynağı |

---

## Abonelik türleri

### 1. `Subscribe(sessionId, onEvent)` → `ApprovalEscalationSubscription`

**Kullanım:** Admin panel — bir session'ın onay ve eskalasyon event'lerini dinler.

Abone olunan kaynaklar ve tetiklenen event tipleri (`StreamEventTypes`):

| Kaynak | Event tipi | Payload |
|--------|-----------|---------|
| `IApprovalQueue.RequestCreated` | `ApprovalRequired` | `ApprovalRequest` (ham) |
| `IApprovalQueue.RequestDecided` | `ApprovalResolved` | `{ id, status, reason, decidedBy }` |
| `IEscalationSink.RequestCreated` | `EscalationCreated` | `EscalationRequest` (ham) |

Sadece `req.SessionId == sessionId` eşleşen event'ler callback'e iletilir.

---

### 2. `SubscribeToChatEvents(sessionId, onEvent)` → `ChatEventSubscription`

**Kullanım:** Admin panel veya müşteri tarafı — bir session'ın mod değişikliklerini ve handoff event'lerini dinler.

Abone olunan kaynaklar:
- `IChatModeRegistry.ModeChanged`
- `IEscalationSink.RequestCreated`
- `IEscalationSink.RequestDecided`

**Üretilen event türleri:**

| Event | Tetikleyici | Açıklama |
|-------|------------|---------|
| `HumanJoined` | ModeChanged → Human | Temsilci oturuma katıldı (`{ sessionId, humanAgent, enteredAt }`) |
| `HumanLeft` | ModeChanged → Bot | Temsilci ayrıldı, bot devreye girdi (`{ sessionId }`) |
| `HandoffPending` | RequestCreated | Eskalasyon isteği oluştu (`{ escalationId, reason, createdAt }`) |
| `HandoffCleared` | RequestDecided (Resolved/Dismissed) **ve** mod zaten Bot ise | Handoff tamamlandı/iptal edildi (`{ escalationId, status }`) |

---

## Kullanım örneği (SSE endpoint'i)

```csharp
using var subscription = hitlEvents.Subscribe(
    sessionId,
    (eventType, data) => sse.WriteAsync(eventType, data));

// subscription Dispose edildiğinde tüm event kaynaklarından otomatik unsubscribe olunur
```

Gerçek kullanım: `CustomerSupportBot.Api/Endpoints/ChatEndpoints.cs` (stream chat) ve `CustomerSupportBot.Api/Services/ChatEventOrchestrator.cs` (kalıcı `/chat/events/{sessionId}` bağlantısı).

---

## HitlEventPortService ile diğer servisler arasındaki ilişki

```
IApprovalQueue.RequestCreated/RequestDecided ───┐
IEscalationSink.RequestCreated/RequestDecided ──┤→ HitlEventPortService → onEvent callback → SSE → Client
IChatModeRegistry.ModeChanged ──────────────────┘
```
