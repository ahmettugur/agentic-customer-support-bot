# InMemory Adaptörler

`InMemory/` altındaki tüm adaptörler tek process içinde çalışır. Kalıcılık yoktur — restart sonrası veri sıfırlanır. Geliştirme, test ve demo senaryoları için kullanılır.

> 💡 **Analiz notu:** Test yazarken Postgres kurmak zahmetli. InMemory adapter'lar aynı port'u (interface'i) implement eder ama `ConcurrentDictionary` ile bellekte tutar. Bu sayede unit testler milisaniyeler içinde çalışır.

---

## InMemorySessionManager

**Port:** `ISessionManager`

`AgentSession` ve `ConversationMessage` listelerini `ConcurrentDictionary` ile tutar.

| Metod | Açıklama |
| ------- | --------- |
| `GetOrCreate(sessionId)` | Session varsa döner, yoksa oluşturur |
| `Get(sessionId)` | Nullable döner |
| `Update(session)` | State günceller |
| `GetHistory(sessionId)` | Tüm mesaj listesi |
| `AddExchangeAsync(sessionId, query, response, signals?)` | Kullanıcı + asistan mesajı ekler; `signals` (`TurnSignals?`) reasoning'in bu tur için ürettiği intent/sentiment'i taşır — bkz. aşağıdaki not |
| `AppendUserMessage` / `AppendAssistantMessage` | Tekli mesaj ekler |
| `ExtractAndUpdateState` | `SessionStateExtractor` ile state türetir, kaydeder |
| `MutateStateAsync` | Delegate ile atomic state mutasyonu |
| `GetAll` | Tüm session listesi (metadata) |
| `ClearSession` | Geçmişi siler, state sıfırlar |

> **`AddExchangeAsync`, turun türetilmiş state'inin (intent, sentiment, `ConsecutiveNegativeTurns`, phase) TEK yazarına** — `SessionStateExtractor.ExtractAndApply` (Domain katmanı) — giden tek kapıdır. İçeride `ExtractAndApply`'ı `session` nesnesi üzerinde `lock` altında çağırır: `ConsecutiveNegativeTurns` bir oku-değiştir-yaz işlemi olduğundan, aynı session'a çakışan eşzamanlı isteklerde (çift-submit, çoklu sekme) kilitsiz çağrı bir artışı kaybettirebilirdi. `GetOrCreateAsync`/`GetAsync` aynı `sessionId` için hep AYNI `AgentSession` referansını döndürdüğünden `session` nesnesi kilit anahtarı olarak güvenle kullanılır. Detay ve "neden `signals`" sorusunun cevabı: [`Services-SessionStateExtractor.md`](../CustomerSupportBot.Domain/Services/SessionStateExtractor.md).

---

## InMemoryApprovalQueue

**Port:** `IApprovalQueue`

`TaskCompletionSource<bool>` başına bir pending task ile asenkron onay bekleme mekanizması sağlar.

| Özellik | Değer |
| --------- | ------- |
| Ring buffer | Max 200 history |
| Timeout | Configurable; timeout'ta `AutoApprove`/`AutoReject`/`None` |
| Thread safety | `SemaphoreSlim(1,1)` |
| Events | `RequestCreated`, `RequestDecided` |

**`AwaitDecisionAsync`:** `TaskCompletionSource<ApprovalRequest>` bekler. `DecideAsync(id, approved)`
çağrısında TCS tamamlanır, timeout Task ile yarışır.

> ⚠️ Bu **eski bloklayan yolun** metodudur ve onay gerektiren dört tool artık onu **çağırmaz**
> (bkz. `ApprovalGateService.ExecuteWithApprovalGateAsync`). Yeni akışta tool kararı beklemez;
> gerçek iş admin karar verdiğinde `IApprovalExecutionRouter` üzerinden çalışır. Metot geriye
> dönük uyumluluk için duruyor.

---

## InMemoryChatBridge

**Port:** `IChatBridge`

Per-session `System.Threading.Channels.Channel<BridgeMessage>` kullanır. Admin ve kullanıcı kanalları ayrıdır.

| Metod | Kanal | DB'ye yazılır? |
| ------- | ------- | --------------- |
| `PublishUserMessage` | her ikisi | Evet |
| `PublishBotMessage` | her ikisi | Evet |
| `PublishAdminMessage` | her ikisi | Evet |
| `PublishSystemMessage` | her ikisi | Evet |
| `PublishAdminOnlyMessage` | sadece admin | Evet |
| `PublishBotTyping` | her ikisi | **Hayır** (geçici) |
| `RecordBotExchange` | — | Kayıt için |

Ring buffer: 200 mesaj geçmişi; `SubscribeToUserAsync` / `SubscribeToAdminAsync` IAsyncEnumerable döner.

---

## InMemoryChatModeRegistry

**Port:** `IChatModeRegistry`

`ConcurrentDictionary<sessionId, ChatSessionMode>` ile mod durumu tutar.

| Metod | Açıklama |
| ------- | --------- |
| `SetMode(sessionId, Bot/Human, agentId)` | Mod ata + event fire |
| `GetMode(sessionId)` | Mevcut mod |
| `GetActive()` | Human modundaki session'lar |
| Event: `ModeChanged` | TakeOver/Release'de tetiklenir |

---

## InMemoryEscalationSink

**Port:** `IEscalationSink`

Ring buffer: 500 kayıt. `EscalationStateFactory` state machine geçişlerini uygular.

**State geçişleri:**

```
Open → Acknowledged (TakeOver)
Open → Resolved     (Release / ReplanEscalation)
Open → Dismissed    (DismissOrphaned)
```

| Metod | Açıklama |
| ------- | --------- |
| `Create(...)` | Yeni eskalasyon, event fire |
| `GetOpen(sessionId?)` | Açık kayıtlar |
| `GetRecent(count)` | Son N kayıt |
| `Decide(id, resolved, decidedBy, note)` | Karar ver |
| `Reassign(id, toAgentId)` | Yeniden atama |
| `LastEmittedAt(kind, targetId, severity)` | SLA duplicate önleme |

---

## InMemoryHumanAgentRegistry

**Port:** `IHumanAgentRegistry`

`RoutingOptions.DefaultAgents`'tan seed edilir.

| Metod | Açıklama |
| ------- | --------- |
| `GetActive()` | `IsActive == true` agent'lar |
| `GetAll()` | Tüm agent'lar |
| `Create` / `Update` / `Delete` | CRUD |
| `IncrementLoad(agentId)` | Per-agent lock ile |
| `DecrementLoad(agentId)` | Negatife düşmez |
| `GetLinkedUsersAsync()` | InMemory'de boş döner |

---

## InMemoryRatingStore

**Port:** `IRatingStore`

`ConcurrentDictionary<sessionId, ConversationRating>`.

| Metod | Açıklama |
| ------- | --------- |
| `Submit(sessionId, stars, feedback)` | Upsert |
| `GetBySession(sessionId)` | Nullable |
| `GetAll()` | Timestamp DESC sıralı |
| `GetRecent(count)` | Son N |

---

## InMemoryReasoningTraceStore

**Port:** `IReasoningTraceStore`

Ring buffer: 500. Üç aşamalı kayıt:

| Metod | Açıklama |
| ------- | --------- |
| `StartTrace(traceId, sessionId, query)` | Skeleton oluştur |
| `Update(trace)` | Cache'i güncelle (DB yok) |
| `Complete(trace)` | Tam state kaydet |
| `Get(traceId)` | Tekil |
| `GetRecent(count)` | Son N |
| `GetBySession(sessionId)` | Session'a göre |

---

## InMemorySlaEventSink

**Port:** `ISlaEventSink`

Ring buffer: 500.

| Metod | Açıklama |
| ------- | --------- |
| `Record(event)` | Event ekle |
| `GetRecent(count)` | Son N |
| `LastEmittedAt(kind, targetId, severity)` | Aynı olayın son emit zamanı (duplicate önleme) |

---

## InMemoryCustomerProfileStore

**Port:** `ICustomerProfileStore`

| Metod | Açıklama |
| ------- | --------- |
| `GetOrCreate(customerId)` | Yeni profile oluşturur |
| `Get(customerId)` | Nullable |
| `Upsert(profile)` | Ekle/güncelle |
| `Delete(customerId)` | Sil |
| `List(take)` | `LastInteractionAt` DESC sıralı, ilk N |
| `Count` | Toplam profil sayısı |

---

## InMemoryLessonStore

**Port:** `ILessonStore`

| Metod | Açıklama |
| ------- | --------- |
| `Add(lesson)` | Ekle |
| `Get(id)` | Tekil |
| `Update(lesson)` | Güncelle |
| `GetByStatus(status)` | Status filtreli |
| `GetAll(int limit = 200)` | `CreatedAt` DESC sıralı, ilk N |

---

## InMemoryMessageBusAdapter

**Port:** `IMessageBusPort`

İşlem-içi pub/sub. `Action<T>` handler'larla abone olma, `Publish(channel, message)` ile yayın.
