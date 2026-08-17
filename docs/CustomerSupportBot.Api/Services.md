# Services — ChatEventOrchestrator

**Dosya:** `Services/ChatEventOrchestrator.cs`
**Yaşam döngüsü:** Scoped (her request başına yeni instance)

`/chat/events/{sessionId}` endpoint'inin arkasındaki orkestratör — per-session, **uzun ömürlü SSE bağlantısı** kurar ve birden fazla event kaynağını tek stream'e birleştirir.

---

## Neden var?

Browser bir chat session'ına bağlandığında **birden fazla event tipini** dinlemek ister:

- Bot yazıyor göstergesi
- Admin'in canlı takeover mesajları
- Escalation oluştu bildirimi
- Bot moduna geri dönüş

Bu event'ler farklı port'lardan (`IChatSessionPort`, `IHitlEventPort`) gelir. Her browser bağlantısı için **tek SSE stream'e** harmanlamak gerekir.

---

## Constructor injection

```csharp
public sealed class ChatEventOrchestrator(
    IChatSessionPort chatSession,
    IHitlEventPort hitlEvents,
    IHostApplicationLifetime appLifetime,
    ILogger<ChatEventOrchestrator> logger)
```

`IHostApplicationLifetime` — uygulama shutdown anında orphan escalation temizliği yapılmaması için kontrol edilir.

---

## `ExecuteAsync`

```csharp
public async Task ExecuteAsync(string sessionId, SseForwarder sse, CancellationToken ct)
{
    // 1. İlk session event'i
    await sse.WriteSessionAsync(sessionId);

    // 2. Mevcut HITL state snapshot'ı
    await SendInitialStateAsync(sessionId, sse);

    // 3. HITL event'lere abone ol
    using var subscription = hitlEvents.SubscribeToChatEvents(
        sessionId,
        (eventType, data) => sse.WriteAsync(eventType, data));

    // 4. Bridge mesajlarını drain et
    await ProcessBridgeMessagesAsync(sessionId, sse, ct);

    // 5. Disconnect → orphan escalation temizliği (shutdown değilse)
    if (!appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        var dismissed = chatSession.DismissOrphanedEscalations(sessionId);
    }
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

```csharp
private async Task SendInitialStateAsync(string sessionId, SseForwarder sse)
{
    var state = chatSession.GetStateOrDefault(sessionId);

    if (state.Mode == ChatMode.Human)
    {
        await sse.WriteAsync(StreamEventTypes.HumanJoined, new
        {
            sessionId,
            humanAgent = state.HumanAgent ?? WellKnown.Defaults.Admin,
            enteredAt = state.EnteredAt
        });
    }
    else
    {
        var pending = chatSession.GetOpenEscalations()
            .FirstOrDefault(e => e.SessionId == sessionId);

        if (pending != null)
        {
            await sse.WriteAsync(StreamEventTypes.HandoffPending, new
            {
                escalationId = pending.Id,
                reason = pending.Reason,
                createdAt = pending.CreatedAt
            });
        }
    }
}
```

Bu sayede yeni bağlanan browser **eski olayları kaçırmaz** — server'ın bildiği state ile başlar.

### 3. HITL subscription

`IHitlEventPort.SubscribeToChatEvents(sessionId, callback)` — session'a özel event dinleyicisi. `using` bloğu — endpoint sona erince subscription'dan unsubscribe (memory leak önlenir).

### 4. Bridge mesajları

```csharp
private async Task ProcessBridgeMessagesAsync(string sessionId, SseForwarder sse, CancellationToken ct)
{
    await foreach (var msg in chatSession.SubscribeToUserAsync(sessionId, ct))
    {
        if (msg.Sender == ChatBridgeSender.BotTyping)
        {
            await sse.WriteAsync(StreamEventTypes.BotTyping, new
            {
                sessionId,
                on = string.Equals(msg.Text, "on", StringComparison.OrdinalIgnoreCase)
            });
            continue;
        }

        await sse.WriteAsync(StreamEventTypes.HumanMessage, new
        {
            id = msg.Id,
            from = msg.Sender.ToString().ToLowerInvariant(),
            humanAgent = msg.HumanAgent,
            text = msg.Text,
            timestamp = msg.Timestamp
        });
    }
}
```

`SubscribeToUserAsync` IAsyncEnumerable — yeni mesaj gelir gelmez yield eder.

### 5. Orphan escalation temizliği

Browser disconnect olduğunda bekleyen escalation'lar **otomatik dismiss edilir** (uygulama shutdown değilse):

```csharp
var dismissed = chatSession.DismissOrphanedEscalations(sessionId);
if (dismissed > 0)
    logger.LogInformation("... {Count} eskalasyon otomatik kapatıldı. session={Session}", dismissed, sessionId);
```

Müşteri oturumu kapattıysa admin paneline pending escalation göstermek anlamsız.

---

## Yayılan event tipleri

| Event type | Trigger | Payload |
|---|---|---|
| `session` | Bağlantı kuruldu | `{ sessionId }` |
| `human_joined` | Human mode aktif (initial state) | `{ sessionId, humanAgent, enteredAt }` |
| `handoff_pending` | Pending escalation var (initial state) | `{ escalationId, reason, createdAt }` |
| `bot_typing` | Bot yazıyor sinyali | `{ sessionId, on: bool }` |
| `human_message` | Bridge mesajı | `{ id, from, humanAgent?, text, timestamp }` |
| HITL event'ler | `IHitlEventPort` subscription | Dynamic (humanJoined, humanLeft, vb.) |

---

## Scoped neden?

```csharp
services.AddScoped<ChatEventOrchestrator>();
```

Her request kendi instance'ını alır. Bu instance request boyunca yaşar — uzun süreli SSE bağlantısı boyunca.

Singleton olsaydı birden fazla browser farklı session ID'lerle bağlanır, tek instance state'i karıştırır. Scoped DI hem encapsulation hem cleanup sağlar (request bitince dispose edilir).

---

## CancellationToken kullanımı

`ct` request'in `HttpContext.RequestAborted` token'ı — browser disconnect olunca cancel olur.

- `await foreach (msg in ...).WithCancellation(ct)` — IAsyncEnumerable durur
- `OperationCanceledException` yakalanır, debug log yazılır
- `finally` bloğu (orphan temizliği) çalışır

---

## Akış örneği

```
Browser    ────GET /chat/events/sess-123────→  Api endpoint
                                                  ↓
                                            ChatEventOrchestrator.ExecuteAsync()
                                                  ↓
   ← event: session {sessionId:"sess-123"}
   ← event: human_joined {humanAgent:"Ali"}      ← initial state

      (insan Ali yazıyor)
   ← event: human_message {from:"admin", text:"Merhaba..."}

      (Ali ayrıldı — HITL subscription event)
   ← event: humanLeft {leftAt:...}

      (bot otomatik yanıt)
   ← event: bot_typing {on:true}
   ← event: human_message {from:"bot", text:"Yanıt..."}

   Browser ──disconnect──→
                                            DismissOrphanedEscalations()
```

---

## Bağlantılar

- [Infrastructure.md](Infrastructure.md) — SseForwarder thread safety
- [Endpoints-Chat.md](Endpoints-Chat.md) — `/chat/events/{sessionId}` endpoint
- [Application HitlEventPortService](../CustomerSupportBot.Application/Escalation/HitlEventPortService.md)
