# InMemory Adaptörler

`InMemory/` altındaki tüm adaptörler tek process içinde çalışır. Kalıcılık yoktur — restart sonrası veri sıfırlanır. Geliştirme, test ve demo senaryoları için kullanılır.

---

## InMemorySessionManager

**Port:** `ISessionManager`

`AgentSession` ve `ConversationMessage` listelerini `ConcurrentDictionary` ile tutar.

| Metod | Açıklama |
|-------|---------|
| `GetOrCreate(sessionId)` | Session varsa döner, yoksa oluşturur |
| `Get(sessionId)` | Nullable döner |
| `Update(session)` | State günceller |
| `GetHistory(sessionId)` | Tüm mesaj listesi |
| `AddExchange(sessionId, query, response)` | Kullanıcı + asistan mesajı ekler |
| `AppendUserMessage` / `AppendAssistantMessage` | Tekli mesaj ekler |
| `ExtractAndUpdateState` | `SessionStateExtractor` ile state türetir, kaydeder |
| `MutateStateAsync` | Delegate ile atomic state mutasyonu |
| `GetAll` | Tüm session listesi (metadata) |
| `ClearSession` | Geçmişi siler, state sıfırlar |

---

## InMemoryApprovalQueue

**Port:** `IApprovalQueue`

`TaskCompletionSource<bool>` başına bir pending task ile asenkron onay bekleme mekanizması sağlar.

| Özellik | Değer |
|---------|-------|
| Ring buffer | Max 200 history |
| Timeout | Configurable; timeout'ta `AutoApprove`/`AutoReject`/`None` |
| Thread safety | `SemaphoreSlim(1,1)` |
| Events | `RequestCreated`, `RequestDecided` |

**`AwaitDecisionAsync`:** `TaskCompletionSource<bool>` bekler. `Decide(id, approved)` çağrısında TCS tamamlanır. Timeout Task ile yarışır.

---

## InMemoryChatBridge

**Port:** `IChatBridge`

Per-session `System.Threading.Channels.Channel<BridgeMessage>` kullanır. Admin ve kullanıcı kanalları ayrıdır.

| Metod | Kanal | DB'ye yazılır? |
|-------|-------|---------------|
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
|-------|---------|
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
|-------|---------|
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
|-------|---------|
| `GetActive()` | `IsActive == true` agent'lar |
| `GetAll()` | Tüm agent'lar |
| `Create` / `Update` / `Delete` | CRUD |
| `IncrementLoad(agentId)` | Per-agent lock ile |
| `DecrementLoad(agentId)` | Negatife düşmez |
| `GetLinkedUsersAsync()` | InMemory'de boş döner |

---

## InMemoryOrderAdapter

**Port:** `IOrderRepository`

Demo sipariş verisi (Dell XPS, iPhone, vs.) içerir. Üretimde Postgres gerekirse bağlantı eklenir.

| Metod | Açıklama |
|-------|---------|
| `Create(...)` | Yeni sipariş, artan ID |
| `Get(orderId)` | Tekil sipariş |
| `GetByCustomer(customerId)` | Müşteriye ait siparişler |
| `GetLast(customerId)` | Son sipariş |

---

## InMemoryComplaintAdapter

**Port:** `IComplaintRepository`

Demo şikayet verisi. Create/Get/GetByOrder/GetByCustomer.

---

## InMemoryProductCatalogAdapter

**Port:** `IProductCatalogRepository`

Demo ürün kataloğu (Dell XPS 15, iPhone 15 Pro, vb.).

| Metod | Açıklama |
|-------|---------|
| `FindProduct(name)` | Partial match (case-insensitive) |
| `TryDeductStock(productId, qty)` | Lock ile stok düşürme |
| `GetAll()` | Tüm ürünler |

---

## InMemoryRatingStore

**Port:** `IRatingStore`

`ConcurrentDictionary<sessionId, ConversationRating>`.

| Metod | Açıklama |
|-------|---------|
| `Submit(sessionId, stars, feedback)` | Upsert |
| `GetBySession(sessionId)` | Nullable |
| `GetAll()` | Timestamp DESC sıralı |
| `GetRecent(count)` | Son N |
| `GetSummary()` | Count + Average + Distribution |

---

## InMemoryReasoningTraceStore

**Port:** `IReasoningTraceStore`

Ring buffer: 500. Üç aşamalı kayıt:

| Metod | Açıklama |
|-------|---------|
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
|-------|---------|
| `Record(event)` | Event ekle |
| `GetRecent(count)` | Son N |
| `LastEmittedAt(kind, targetId, severity)` | Aynı olayın son emit zamanı (duplicate önleme) |

---

## InMemoryWorkflowDefinitionStore

**Port:** `IWorkflowDefinitionStore`

| Metod | Açıklama |
|-------|---------|
| `Upsert(def, updatedBy)` | Ekle/güncelle; ID yoksa `Slugify(name)` |
| `Get(id)` | Tekil |
| `GetAll()` | Tümü |
| `GetActive()` | `IsActive == true` |
| `Delete(id)` | Sil |

**Slugify:** İsim küçük harfe çevrilir, Türkçe karakterler normalleştirilir (`ç→c`, `ğ→g`, `ı→i`, `ö→o`, `ş→s`, `ü→u`), alfanümerik olmayan karakterler kaldırılır.

---

## InMemoryCustomerProfileStore

**Port:** `ICustomerProfileStore`

| Metod | Açıklama |
|-------|---------|
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
|-------|---------|
| `Add(lesson)` | Ekle |
| `Get(id)` | Tekil |
| `Update(lesson)` | Güncelle |
| `GetByStatus(status)` | Status filtreli |
| `GetAll()` | `CreatedAt` DESC sıralı |

---

## InMemoryMessageBusAdapter

**Port:** `IMessageBusPort`

İşlem-içi pub/sub. `Action<T>` handler'larla abone olma, `Publish(channel, message)` ile yayın.

---

## InMemorySlaEventSink (SLA)

Bkz. yukarıda. Ring buffer + `LastEmittedAt` duplicate koruması.
