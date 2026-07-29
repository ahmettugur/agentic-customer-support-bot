# ChatPortService

**Dosya:** `Services/Chat/ChatPortService.cs`  
**Implements:** `IChatPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Bir kullanıcı mesajının tüm işlem adımlarını yönetir. `ReasoningService` → `IAgentTeamPort` → `SessionStateService` sırasını uygular. İki modu vardır:

- **`HandleAsync`** — Tek bir string yanıt döner (non-streaming)
- **`HandleStreamAsync`** — `IAsyncEnumerable<StreamEvent>` ile SSE akışı üretir

## Constructor bağımlılıkları

```csharp
public ChatPortService(
    IAgentTeamPort team,                   // MAF workflow (Adapters.Agents)
    IReasoningPort reasoning,              // Reasoning pipeline (bu katman)
    ISessionManager sessions,              // Oturum yöneticisi
    IChatModeRegistry modeRepo,            // Human / Bot mod kaydı
    IChatBridge chatBridge,                // HITL live-chat köprüsü
    SessionStateService sessionState,      // Sentiment + intent güncelleme
    IApprovalContextAccessor approvalContext, // AsyncLocal HITL scope
    ILogger<ChatPortService> logger)
```

## `HandleAsync` (Non-streaming)

```
1. session = GetOrCreate(sessionId)
2. history = GetHistory(sessionId)
3. reasoningResult = ReasonAsync(query, session, history)
4. approvalScope = SetScope(sessionId)    ← HITL tool call'ları bu scope'da kimliğini bilir
5. response = team.RunAsync(query, history, session, reasoning)
6. UpdateSessionIntent(session, intent)
7. AddExchange(sessionId, query, response)
8. return ChatResponse(response, sessionId, reasoning)
```

## `HandleStreamAsync` (Streaming)

### İnsan modu (HITL live takeover)

Bir oturumda admin "Devrala" butonuna basarsa, `IChatModeRegistry.GetMode(sessionId) == ChatMode.Human` olur. Bu durumda:
- Bot bypass edilir — LLM çağrısı yapılmaz
- `HumanJoined` StreamEvent gönderilir
- Kullanıcı mesajı `IChatBridge.PublishUserMessage` ile admin kanalına iletilir

### Bot modu

```
yield: Session event → SessionEventPayload(sessionId)

Reasoning stream:
  yield: ReasoningStart
  yield: ReasoningDelta* (her 20ms'de bir chunk)
  yield: ReasoningComplete → ReasoningResult

approvalScope = SetScope(sessionId)  ← HITL scope açılır

Workflow stream:
  yield: Agent {name, status:"running"}
  yield: Agent {name, status:"done"}
  ...
  yield: ResponseStart
  yield: ResponseDelta*
  yield: ResponseComplete

SessionStateService.PersistExchange(...)  ← Geçmiş kaydedilir

yield: SentimentUpdate
yield: SentimentAlert?  (ConsecutiveNegativeTurns eşiği aşıldıysa)
```

## StreamEvent türleri (özet)

| Tip | Ne zaman? | Data |
|-----|-----------|------|
| `session` | İlk event | `SessionEventPayload(sessionId)` |
| `humanJoined` | İnsan modu aktifse | `{ sessionId, humanAgent, enteredAt }` |
| `reasoningStart` | Reasoning başladığında | null |
| `reasoningDelta` | Reasoning chunk geldiğinde | `{ text }` |
| `reasoningComplete` | Reasoning bittiğinde | `ReasoningResult` |
| `agent` | Ajan başladığında/bittiğinde | `{ name, status }` |
| `responseStart` | Yanıt akışı başladığında | `{ terminationReason }` |
| `responseDelta` | Yanıt chunk'ı | `{ text }` |
| `responseComplete` | Yanıt tamamlandığında | `{ text, terminationReason }` |
| `sentimentUpdate` | Her akış sonunda | `{ sentiment, score, consecutive, sessionId }` |
| `sentimentAlert` | Eşik aşıldığında | `{ sentiment, score, consecutive, sessionId, message }` |

## ApprovalContext kapsamı

```csharp
using var approvalScope = _approvalContext.SetScope(sessionId, null, query);
```

Bu `using` bloğu `ApprovalContextAccessor`'a mevcut `sessionId` ve `userQuery`'yi bildirir. İçeride workflow çalışırken bir HITL tool çağrıldığında, `ApprovalGateService` bu bağlamdan session bilgisini okur ve doğru `ApprovalRequest` oluşturur.

Scope blok dışına çıkınca (`Dispose`) önceki bağlam geri yüklenir — paralel workflow'lar birbirinin bağlamını kirletmez (`AsyncLocal`).

## Sentiment alert mekanizması

`SessionStateService.CheckSentimentAlert(session)` her akışın sonunda çağrılır.

- `ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative` → `ShouldAlert = true`
- `SentimentAlert` event'i gönderilir → Admin paneli bunu alır ve uyarı gösterir

## Text delta extraction

`HandleStreamAsync` içinde `ResponseDelta` event'lerinden metin parçaları toplanır:

```csharp
var text = TryExtractText(evt.Data);
```

`TryExtractText`, `evt.Data`'yı JSON serialize edip `text` property'sini okur. Bu yaklaşım `Data`'nın anonymous object olmasına rağmen çalışır.

## İnsan modunda ne olmaz?

- `ReasoningService` çağrılmaz
- `IAgentTeamPort` çağrılmaz
- `SessionStateService` çağrılmaz
- Kullanıcı mesajı `ISessionManager.AddExchange` ile geçmişe eklenir (boş yanıtla)
- Admin tarafı `IChatBridge` aboneliği ile mesajı alır ve ekranında görür
