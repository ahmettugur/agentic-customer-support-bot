# ChatSessionPortService

**Dosya:** `Services/Chat/ChatSessionPortService.cs`  
**Implements:** `IChatSessionPort`  
**Yaşam döngüsü:** Singleton

> Metodların çoğu **senkrondur** ve `Task`/`CancellationToken` yerine sonuç-record'ları (`ChatSessionTakeoverResult`, `ChatSessionReleaseResult`, `ChatSessionMessageResult`, `ChatSessionReplanResult`) döner — hata durumları exception yerine `ErrorCode`/`ErrorMessage` alanlarıyla taşınır.

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

### `TakeOver`

```csharp
ChatSessionTakeoverResult TakeOver(string sessionId, string humanAgent, string? agentId = null)
```

Human agent oturumu devralır. Senkron; sonucu `ChatSessionTakeoverResult` (`Success`, `ErrorCode`, `ErrorMessage`) olarak döner — exception fırlatmaz.

**Akış:**
```
1. IChatModeRegistry.TakeOver(sessionId, humanAgent)
2. IEscalationSink.GetOpen() içinden bu session'a ait açıkları "Acknowledged" durumuna getir
3. IHumanAgentRegistry.IncrementLoad(agentId)  (agentId verilmişse)
```

---

### `Release`

```csharp
ChatSessionReleaseResult Release(string sessionId, string? agentId = null)
```

Human agent oturumu bırakır. Senkron; sonuç `ChatSessionReleaseResult`.

**Akış:**
```
1. IChatModeRegistry.Release(sessionId)
2. IEscalationSink.GetOpen() içinden bu session'a ait açıkları "Resolved" yap
3. IHumanAgentRegistry.DecrementLoad(agentId)  (agentId verilmişse)
4. IChatBridge.PublishSystemMessage(sessionId, "Temsilci bağlantısı kesildi. Bot devreye alındı.")
```

---

### `SendAdminMessage`

```csharp
ChatSessionMessageResult SendAdminMessage(string sessionId, string humanAgent, string text)
```

Admin panelinden müşteriye mesaj gönderir. Senkron.

**Önkoşul:** Session'ın modu `Human` olmalı. Değilse **exception fırlatmaz** — `ChatSessionMessageResult.ErrorCode = "invalid_state"` döner. `text` boşsa `ErrorCode = "invalid_input"`.

**Akış:**
```
1. IChatModeRegistry.GetMode(sessionId) → Human değilse ErrorCode="invalid_state" ile dön
2. ISessionManager üzerinden mesajı kaydet
3. IChatBridge.PublishAgentMessage(sessionId, humanAgent, text)
```

---

### `ReplanSession`

```csharp
ChatSessionReplanResult ReplanSession(string sessionId, string requestedBy, string? note)
```

Admin note ile (veya son kullanıcı mesajıyla) bot'u yeniden planlama tetikler. Senkron; `requestedBy` zorunludur (audit için).

**Akış:**
```
1. session.State.ForceReplanNextTurn = true
2. session.State.ReplanNote = note (opsiyonel — admin notu varsa query olarak kullanılır)
3. ISessionManager.Update(session)
4. IChatBridge.PublishAdminNote(sessionId, note)  ← sadece admin paneline görünür
5. IReplanService.ExecuteAsync(sessionId)         ← reasoning + workflow pipeline'ı koşturur
```

---

### `ReplanEscalation`

```csharp
ChatSessionReplanResult ReplanEscalation(string escalationId, string requestedBy, string? note)
```

`sessionId` almaz — ilgili session, eskalasyon kaydından türetilir. Belirli eskalasyonu çözer ve ardından `ReplanSession` çağırır.

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
TakeOver(sessionId, humanAgent) →
                               TakeOver(sessionId, humanAgent) → ChatModeRegistry
                               AcknowledgeEscalations()        → EscalationSink
                               IncrementLoad(agentId)          → HumanAgentRegistry

SendAdminMessage(text)       →
                               kaydet                          → SessionManager
                               PublishAgentMessage()            → ChatBridge → Müşteri

Release(sessionId, agentId)  →
                               Release(sessionId)               → ChatModeRegistry
                               ResolveEscalations()             → EscalationSink
                               DecrementLoad(agentId)            → HumanAgentRegistry
                               PublishSystemMessage()            → ChatBridge → Müşteri
```

---

## Yeni bir TakeOver senaryosu eklemek

Oturumu devralmada özel iş mantığı gerekiyorsa `TakeOver` içindeki adımlara müdahale edin — örneğin bir CRM sistemine bildirim göndermek için `IChatBridge` benzer arayüzde yeni bir metot sağlayabilir.
