# HitlEventPortService

**Dosya:** `Services/HitlEventPortService.cs`  
**Implements:** `IHitlEventPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Admin panel ve müşteri tarafı için HITL (Human-in-the-Loop) olay akışlarını sağlar. SSE (Server-Sent Events) üzerinden admin'e gerçek zamanlı bildirim gönderir. İki farklı abonelik türü sunar.

---

## Abonelik türleri

### 1. `Subscribe` → `ApprovalEscalationSubscription`

**Kullanım:** Admin panel — bir session'ın onay ve eskalasyon event'lerini dinler.

```csharp
IHitlEventSubscription Subscribe(string sessionId)
```

Abone olunan event kaynakları:
- `IApprovalPort.ApprovalDecided` — onay kararı alındı
- `IEscalationPort.EscalationCreated` — yeni eskalasyon oluştu
- `IEscalationPort.EscalationDecided` — eskalasyon kararı alındı

Her event `HitlEvent` olarak yayınlanır. Caller `IHitlEventSubscription.EventStream` (IAsyncEnumerable) üzerinden dinler.

---

### 2. `SubscribeToChatEvents` → `ChatEventSubscription`

**Kullanım:** Admin panel veya müşteri tarafı — bir session'ın mod değişikliklerini ve handoff event'lerini dinler.

```csharp
IHitlEventSubscription SubscribeToChatEvents(string sessionId)
```

Abone olunan event kaynakları:
- `IChatModeRegistry.ModeChanged` — Bot↔Human geçişi
- `IEscalationSink.RequestCreated` — eskalasyon oluştu

**Üretilen event türleri:**

| Event | Tetikleyici | Açıklama |
|-------|------------|---------|
| `HumanJoined` | ModeChanged → Human | Temsilci oturuma katıldı |
| `HumanLeft` | ModeChanged → Bot | Temsilci ayrıldı, bot devreye girdi |
| `HandoffPending` | RequestCreated | Eskalasyon isteği oluştu |
| `HandoffCleared` | ModeChanged → Bot | Handoff tamamlandı/iptal edildi |

---

## `IHitlEventSubscription` arayüzü

```csharp
public interface IHitlEventSubscription : IDisposable
{
    IAsyncEnumerable<HitlEvent> EventStream { get; }
}
```

`Dispose()` çağrıldığında event kaynaklarından abonelik iptal edilir (memory leak olmaz).

---

## Kullanım örneği (SSE endpoint'i)

```csharp
// API controller'da
using var subscription = _hitlPort.Subscribe(sessionId);
await foreach (var evt in subscription.EventStream.WithCancellation(ct))
{
    await response.WriteAsync($"data: {JsonSerializer.Serialize(evt)}\n\n");
    await response.Body.FlushAsync(ct);
}
```

---

## HitlEvent modeli

```csharp
public record HitlEvent(
    string Type,          // "HumanJoined", "HandoffPending", vb.
    string SessionId,
    DateTime Timestamp,
    string? AgentId,
    string? Note
);
```

---

## HitlEventPortService ile diğer servisler arasındaki ilişki

```
IApprovalPort ──────────────────────┐
IEscalationPort ────────────────────┤→ HitlEventPortService → SSE → Admin Panel
IChatModeRegistry.ModeChanged ──────┘
IEscalationSink.RequestCreated ─────┘
```
