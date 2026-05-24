# ChatSessionPortService

**Dosya:** `Services/ChatSessionPortService.cs`  
**Implements:** `IChatSessionPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Admin panelinin canlı müşteri oturumlarına erişimini sağlar. Human agent'ların oturuma katılması (TakeOver), ayrılması (Release), mesaj göndermesi ve bot'u yeniden planlama tetiklemesi (Replan) bu servis üzerinden gerçekleşir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `ISessionManager` | Session okuma/yazma |
| `IChatBridge` | Admin ↔ Müşteri mesajlaşma köprüsü (Redis/InMemory pub/sub) |
| `IChatModeRegistry` | Bot/Human mod kaydı |
| `IEscalationSink` | Eskalasyon state yönetimi |
| `IHumanAgentRegistry` | Temsilci yük sayacı |
| `IReplanService` | Bot yeniden planlama pipeline'ı |

---

## Metodlar

### `GetActiveAsync`

Aktif tüm oturumları döner. Session durumu, son mesaj ve metadata içerir.

---

### `GetStateOrDefaultAsync`

Belirli session'ın state'ini döner; session yoksa varsayılan (boş) state döner.

---

### `GetHistoryAsync`

Session konuşma geçmişini döner.

---

### `GetSentimentAsync`

Session'ın güncel sentiment'ini döner (Positive / Neutral / Negative).

---

### `PublishSystemMessageAsync`

`IChatBridge.PublishSystemMessage` çağırır — müşteri tarafına görünür sistem mesajı.

---

### `TakeOverAsync`

```csharp
Task TakeOverAsync(string sessionId, string agentId, CancellationToken ct = default)
```

Human agent oturumu devralır.

**Akış:**
```
1. IChatModeRegistry.SetMode(sessionId, Human, agentId)
2. IEscalationSink.GetOpen(sessionId) → açık eskalasyonları "Acknowledged" durumuna getir
3. IHumanAgentRegistry.IncrementLoad(agentId)
```

---

### `ReleaseAsync`

```csharp
Task ReleaseAsync(string sessionId, string agentId, CancellationToken ct = default)
```

Human agent oturumu bırakır.

**Akış:**
```
1. IChatModeRegistry.SetMode(sessionId, Bot, null)
2. IEscalationSink.GetOpen(sessionId) → açık eskalasyonları "Resolved" yap
3. IHumanAgentRegistry.DecrementLoad(agentId)
4. IChatBridge.PublishSystemMessage(sessionId, "Temsilci bağlantısı kesildi. Bot devreye alındı.")
```

---

### `SendAdminMessageAsync`

```csharp
Task SendAdminMessageAsync(string sessionId, string agentId, string text, CancellationToken ct = default)
```

Admin panelinden müşteriye mesaj gönderir.

**Önkoşul:** Session'ın modu `Human` olmalı. Bot modunda çağrılırsa exception fırlatır.

**Akış:**
```
1. IChatModeRegistry.GetMode(sessionId) → Human değilse exception
2. ISessionManager.AppendMessage(sessionId, role=Agent, label=agentId, text)
3. IChatBridge.PublishAgentMessage(sessionId, agentId, text)
```

---

### `ReplanSessionAsync`

```csharp
Task ReplanSessionAsync(string sessionId, string? note, CancellationToken ct = default)
```

Admin note ile (veya son kullanıcı mesajıyla) bot'u yeniden planlama tetikler.

**Akış:**
```
1. session.State.ForceReplanNextTurn = true
2. session.State.ReplanNote = note (opsiyonel — admin notu varsa query olarak kullanılır)
3. ISessionManager.Update(session)
4. IChatBridge.PublishAdminNote(sessionId, note)  ← sadece admin paneline görünür
5. IReplanService.ExecuteAsync(sessionId)         ← reasoning + workflow pipeline'ı koşturur
```

---

### `ReplanEscalationAsync`

```csharp
Task ReplanEscalationAsync(string sessionId, string escalationId, string? note, CancellationToken ct = default)
```

Belirli eskalasyonu çözer ve ardından `ReplanSessionAsync` çağırır.

---

### `SubscribeToAdminAsync / SubscribeToUserAsync`

Admin panel ve müşteri kanalına SSE aboneliği. `IChatBridge` üzerinden mesajları dinler.

---

### `GetOpenEscalationsAsync`

Session'ın açık eskalasyonlarını döner.

---

### `DismissOrphanedEscalationsAsync`

Atanmış agent olmayan veya süresi dolmuş açık eskalasyonları kapatır.

---

## TakeOver / Release Akışı

```
Admin paneli                ChatSessionPortService              Persistence
────────────────────────────────────────────────────────────────────────────
TakeOver(sessionId, agentId) →
                               SetMode(Human, agentId)   → ChatModeRegistry
                               AcknowledgeEscalations()  → EscalationSink
                               IncrementLoad(agentId)    → HumanAgentRegistry

SendAdminMessage(text)      →
                               AppendMessage()           → SessionManager
                               PublishAgentMessage()     → ChatBridge → Müşteri

Release(sessionId, agentId) →
                               SetMode(Bot, null)        → ChatModeRegistry
                               ResolveEscalations()      → EscalationSink
                               DecrementLoad(agentId)    → HumanAgentRegistry
                               PublishSystemMessage()    → ChatBridge → Müşteri
```

---

## Yeni bir TakeOver senaryosu eklemek

Oturumu devralmada özel iş mantığı gerekiyorsa `TakeOverAsync` içindeki adımlara müdahale edin — örneğin bir CRM sistemine bildirim göndermek için `IChatBridge` benzer arayüzde yeni bir metot sağlayabilir.
