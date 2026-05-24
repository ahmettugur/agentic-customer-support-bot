# ChatEventOrchestrator

**Dosya:** `Services/ChatEventOrchestrator.cs`  
**Yaşam döngüsü:** Scoped (her request başına yeni instance)

`/chat/events/{sessionId}` endpoint'inin arkasındaki orkestratör — per-session, **uzun ömürlü SSE bağlantısı** kurar ve birden fazla event kaynağını tek stream'e birleştirir.

---

## Neden var?

Browser bir chat session'ına bağlanmak istediğinde **birden fazla event tipini** dinlemek ister:

- Bot yazıyor göstergesi
- Admin'in canlı takeover mesajları
- Escalation oluştu bildirimi
- Bot moduna geri dönüş

Bu event'ler farklı port'lardan (`IChatSessionPort`, `IHitlEventPort`) gelir. Her browser bağlantısı için **tek SSE stream'e** harmanlamak gerekir.

---

## `ExecuteAsync`

```csharp
public async Task ExecuteAsync(string sessionId, SseForwarder sse, CancellationToken ct)
{
    // 1. İlk session event'i
    await sse.WriteSessionAsync(sessionId, ct);

    // 2. Mevcut HITL state snapshot'ı
    await SendInitialHitlStateAsync(sessionId, sse, ct);

    // 3. HITL event'lere abone ol
    using var subscription = _hitlEventPort.SubscribeToSession(sessionId, evt =>
    {
        _ = sse.WriteAsync(MapEventType(evt), evt, ct);
    });

    // 4. Bridge mesajlarını drain et
    await ProcessBridgeMessagesAsync(sessionId, sse, ct);

    // 5. Disconnect → orphan escalation temizliği
    await _chatSession.DismissOrphanedEscalations(sessionId);
}
```

### 1. Initial session event

İlk açılışta browser session ID'sini onaylar:

```
event: session
data: { "sessionId": "abc-123" }
```

### 2. Initial HITL state

Browser bağlandığında **zaten devam eden bir state** olabilir:
- Bir insan agent zaten chat'e katılmış (HumanJoined)
- Pending bir handoff var (HandoffPending)

```csharp
private async Task SendInitialHitlStateAsync(string sessionId, SseForwarder sse, CancellationToken ct)
{
    var state = await _chatSession.GetStateAsync(sessionId);
    if (state.Mode == ChatMode.Human)
    {
        await sse.WriteAsync(StreamEventTypes.HumanJoined, new
        {
            enteredAt = state.EnteredAt,
            humanAgent = state.HumanAgent
        }, ct);
    }

    var pendingEscalation = await _escalation.GetPendingForSessionAsync(sessionId);
    if (pendingEscalation != null)
    {
        await sse.WriteAsync(StreamEventTypes.HandoffPending, new
        {
            escalationId = pendingEscalation.Id,
            reason = pendingEscalation.Reason,
            createdAt = pendingEscalation.CreatedAt
        }, ct);
    }
}
```

Bu sayede yeni bağlanan browser **eski olayları kaçırmaz** — server'ın bildiği state ile başlar.

### 3. HITL subscription

`IHitlEventPort.SubscribeToSession(sessionId, callback)` — session'a özel event dinleyicisi:

```csharp
using var subscription = _hitlEventPort.SubscribeToSession(sessionId, evt =>
{
    var eventType = evt switch
    {
        HumanJoinedEvent _      => StreamEventTypes.HumanJoined,
        HumanLeftEvent _        => StreamEventTypes.HumanLeft,
        HandoffPendingEvent _   => StreamEventTypes.HandoffPending,
        HandoffClearedEvent _   => StreamEventTypes.HandoffCleared,
        _                       => "unknown"
    };
    _ = sse.WriteAsync(eventType, evt, ct);
});
```

`using` bloğu — endpoint sona erince subscription'dan unsubscribe (memory leak önlenir).

### 4. Bridge mesajları

`ProcessBridgeMessagesAsync` user-facing kanaldan gelen mesajları akıtır:

```csharp
await foreach (var msg in _chatSession.SubscribeToUserAsync(sessionId).WithCancellation(ct))
{
    if (msg.Sender == ChatBridgeSender.BotTyping)
    {
        await sse.WriteAsync(StreamEventTypes.BotTyping, new { on = msg.Text == "on" }, ct);
    }
    else
    {
        await sse.WriteAsync(StreamEventTypes.HumanMessage, new
        {
            id = msg.Id,
            from = msg.Sender.ToString().ToLowerInvariant(),
            humanAgent = msg.Metadata?["humanAgent"],
            text = msg.Text,
            timestamp = msg.At
        }, ct);
    }
}
```

`SubscribeToUserAsync` IAsyncEnumerable — yeni mesaj gelir gelmez yield eder.

### 5. Orphan escalation temizliği

Browser disconnect olduğunda bekleyen escalation'lar **otomatik dismiss edilir**:

```csharp
finally
{
    await _chatSession.DismissOrphanedEscalations(sessionId);
}
```

Niye? Browser kullanıcı kapattıysa müşteri muhtemelen meşgul — admin paneline pending escalation göstermek anlamsız. Otomatik temizlik.

---

## Yayılan event tipleri

| Event type | Trigger | Payload |
|---|---|---|
| `session` | Bağlantı kuruldu | `{ sessionId }` |
| `humanJoined` | İnsan agent TakeOver yaptı | `{ enteredAt, humanAgent }` |
| `humanLeft` | İnsan agent Release yaptı | `{ leftAt, humanAgent }` |
| `handoffPending` | Escalation oluşturuldu | `{ escalationId, reason, createdAt }` |
| `handoffCleared` | Escalation çözüldü/dismiss edildi | `{ escalationId }` |
| `botTyping` | Bot yazıyor sinyali | `{ on: bool }` |
| `humanMessage` | Kullanıcı kanalına mesaj | `{ id, from, humanAgent?, text, timestamp }` |
| `done` | Bağlantı kapatıldı | `{ sessionId }` |

---

## Akış örneği

```
Browser    ────GET /chat/events/sess-123────→  Api endpoint
                                                  ↓
                                            ChatEventOrchestrator.ExecuteAsync()
                                                  ↓
   ← event: session {sessionId:"sess-123"}
   ← event: humanJoined {humanAgent:"Ali"}      ← initial state

      (insan Ali yazıyor)
   ← event: humanMessage {from:"admin", text:"Merhaba..."}

      (Ali ayrıldı)
   ← event: humanLeft {leftAt:...}

      (bot otomatik yanıt)
   ← event: botTyping {on:true}
   ← event: humanMessage {from:"bot", text:"Yanıt..."}

   Browser ──disconnect──→
                                            DismissOrphanedEscalations()
```

---

## Scoped neden?

```csharp
services.AddScoped<ChatEventOrchestrator>();
```

Her request kendi instance'ını alır. Bu instance request boyunca yaşar — uzun süreli SSE bağlantısı boyunca.

Eğer Singleton olsaydı:
- Birden fazla browser farklı session ID'lerle bağlanır
- Tek instance state'i karıştırır

Scoped DI hem encapsulation hem cleanup sağlar (request bitince dispose edilir).

---

## CancellationToken kullanımı

```csharp
public async Task ExecuteAsync(string sessionId, SseForwarder sse, CancellationToken ct)
```

`ct` request'in `HttpContext.RequestAborted` token'ı — browser disconnect olunca cancel olur.

- `await foreach (msg in ...) .WithCancellation(ct)` — IAsyncEnumerable durur
- `sse.WriteAsync(...)` — yazım bitirilir
- `finally` bloğu (orphan temizliği) çalışır

Düzgün cleanup için her await `ct` taşıması kritik.

---

## Bağlantılar

- [Infrastructure.md](Infrastructure.md) — SseForwarder thread safety
- [Endpoints-Chat.md](Endpoints-Chat.md) — `/chat/events/{sessionId}` endpoint
- [Application HitlEventPortService](../application/HitlEventPortService.md)
