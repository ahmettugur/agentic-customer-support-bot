# CustomerSupportTeam

**Dosya:** `CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs`  
**Implements:** `IAgentTeamPort` (Application katmanı portu)  
**Yaşam döngüsü:** Singleton

## Ne yapar?

`CustomerSupportTeam`, 6 ajanı bir araya getiren ve tüm workflow orkestrasyon mantığını yöneten ana sınıftır. Uygulama katmanındaki `IChatPort`, bir kullanıcı mesajını işlemek için bu sınıfın `RunAsync` veya `RunStreamingAsync` metodlarını çağırır.

**Tek cümleyle:** "Kullanıcının mesajını al → doğru ajanları sırayla çalıştır → temiz bir yanıt döndür."

## 6 Ajan

| # | Ajan | İsim sabiti | Tool'lar |
|---|------|-------------|---------|
| 1 | PlanningAgent | `WellKnown.AgentNames.Planning` | Yok (yalnızca yönlendirme) |
| 2 | ProductAgent | `WellKnown.AgentNames.Product` | `product_inquiry_tool` + `product_list_tool` |
| 3 | OrderAgent | `WellKnown.AgentNames.Order` | `order_placement_tool` (HITL) + `order_status_tool` + `get_last_order_tool` + `get_all_orders_tool` + `order_cancel_tool` (HITL) + `return_request_tool` (HITL) |
| 4 | ComplaintAgent | `WellKnown.AgentNames.Complaint` | `complaint_registration_tool` (HITL) |
| 5 | HumanHandoffAgent | `WellKnown.AgentNames.HumanHandoff` | `human_handoff_tool` |
| 6 | ResponseAgent | `WellKnown.AgentNames.Response` | Yok (yalnızca biçimlendirme) |

> **HITL nedir?** `order_placement_tool`, `order_cancel_tool`, `return_request_tool` ve `complaint_registration_tool` direkt çalışmaz; önce `ApprovalGateService` üzerinden admin onayı bekler. Onay gelmezse tool reddedilir.

## Constructor bağımlılıkları

```csharp
public CustomerSupportTeam(
    IChatClient chatClient,                    // LLM istemcisi (OpenAI / Anthropic / Azure)
    IContextPipeline contextPipeline,          // Oturum bağlam sağlayıcıları
    IOptions<WorkflowGuardOptions> guardOptions, // Timeout, max iterasyon vb. korumalar
    IOptions<ParallelExecutionOptions> parallelOptions, // Compound query paralellik ayarları
    IReasoningTraceStore traceStore,           // Trace kayıt deposu
    IPromptRepository prompts,                 // Prompt dosyaları okuyucu
    ApprovalGateService approvalGate,          // HITL onay kapısı
    ICustomerSupportToolsService tools,        // Tool implementasyonları
    IUiHintEmitter uiHint,                     // UI ipuçları yayıcı (kategori seçici vb.)
    ILoggerFactory loggerFactory,
    ISemanticMemoryWriter? semanticMemory = null, // Opsiyonel: episodik bellek (semantic search)
    ICustomerProfileService? profileService = null) // Opsiyonel: müşteri profili güncelleyici
```

> `ISemanticMemoryWriter` ve `ICustomerProfileService` opsiyoneldir — null gelirse özellik sessizce atlanır.
> `IUiHintEmitter` zorunludur — tool çağrıları sırasında biriken UI ipuçlarını (örn. `category_picker`) streaming event olarak yayınlamak için kullanılır.

## Ana metodlar

### `RunAsync` (Non-streaming)

```csharp
public async Task<string> RunAsync(
    string query,
    List<ConversationMessage>? conversationHistory = null,
    AgentSession? session = null,
    ReasoningResult? reasoning = null,
    CancellationToken ct = default)
```

1. Compound query mi? → `RunDecomposedAsync` çağrılır (aşağıda açıklandı) — `ct` içeri taşınır.
2. Workflow mesajları oluşturulur (`BuildWorkflowMessagesAsync`).
3. `RunStreamingAsync` ile aynı timeout deseni kurulur: `timeoutCts` (`WorkflowGuardOptions.TimeoutSeconds`) + dışarıdan gelen `ct` `CreateLinkedTokenSource` ile birleştirilir.
4. Yeni bir `Workflow` + `InProcessExecution` başlatılır; olay akışı `EnumerateWorkflowEventsSafely` ile bu birleşik token'a bağlı olarak tüketilir.
5. `WorkflowOutputEvent` yakalanır, sonuç çıkarılır.
6. Timeout dolarsa `TimeoutException`, dış `ct` iptal edilirse `OperationCanceledException`, workflow hata verirse `InvalidOperationException` fırlatılır (hepsi `ExceptionTranslator.Translate` ile domain exception'a çevrilir). Her üç durumda trace `_traceStore.Complete(...)` ile `timeout` / `cancelled` / `error` nedeniyle kapatılır.
7. `TERMINATE` marker'ları ve teknik JSON blokları temizlenir.
8. Sonuç agent routing mesajı içeriyorsa `RewriteRoutingMessageAsync` ile kullanıcı dostu hale getirilir.
9. `FinalizeTraceAsync` çağrılır — eskalasyon işleme, agent visit çıktıları, episodik bellek ve müşteri profili güncellemesi.

> **Önceki davranış (1):** `RunAsync` hiçbir `CancellationToken` almıyor ve timeout uygulamıyordu — LLM sağlayıcısı asılırsa (özellikle `EvaluationRunner`/`ReplanService` gibi non-streaming çağıranlar için) süresiz beklerdi. Artık `RunStreamingAsync` ile birebir aynı koruma altındadır.
>
> **Önceki davranış (2):** `RunAsync` **hiç trace üretmiyordu**. Bu yüzden `POST /chat/`, `EvaluationRunner` ve `ReplanService` üzerinden gelen her şey gözlemlenebilirlik açısından kördü: agent visit yok, planning JSON'u yok, `ProcessPendingEscalations` çağrılmadığı için eskalasyon kaydı oluşmuyordu, episodik bellek ve müşteri profili güncellenmiyordu. Değerlendirme (evaluation) koşularının trace üretmemesi sonuçların incelenememesine yol açıyordu. Artık iki yol da aynı trace altyapısını paylaşır.

### Paylaşılan trace altyapısı

`RunAsync` ve `RunStreamingAsync` trace toplama mantığını üç ortak parçada paylaşır:

| Üye | Görev |
|---|---|
| `TraceState` (private sealed class) | Tek koşunun trace durumu: `Trace`, `ActiveVisits`, `LastAgentSignature`, `IterationCount`, `Result` |
| `StartTraceState(session, query, reasoning)` | Trace'i açar, `reasoning` varsa iliştirir |
| `ApplyTraceEvent(st, evt)` | Bir workflow event'inin trace yan etkilerini uygular. Dönüş `(Name, Status)?` — null değilse çağıran bunu `Agent` stream event'i olarak yayınlar, null ise event iç/mükerrer olduğu için gözlemlenebilir değişiklik üretmemiştir |
| `FinalizeTraceAsync(st, session, query, result, reason)` | Eskalasyon + agent visit çıktıları + episodik bellek + profil + `Complete` |

Bu sayede `ExecutorInvokedEvent`/`ExecutorCompletedEvent` dedupe'u, 0ms pasif tur filtresi ve iç executor gizlemesi tek yerde tanımlıdır — streaming yol yalnızca `yield` sorumluluğunu ekler.

### `RunStreamingAsync` (SSE Streaming)

```csharp
public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
    string query,
    List<ConversationMessage>? conversationHistory = null,
    AgentSession? session = null,
    ReasoningResult? reasoning = null,
    CancellationToken ct = default)
```

`RunAsync` ile aynı adımları izler, fakat her adımda `StreamEvent` nesneleri yield eder. Frontend bu event'leri SSE üzerinden alır ve arayüzü gerçek zamanlı günceller.

**Üretilen StreamEvent tipleri:**

| Tip | Ne zaman? | Payload |
|-----|-----------|---------|
| `Agent` | Her ajan başladığında/bittiğinde | `{ name, status: "running"\|"done" }` |
| `UiHint` | Tool çağrısı sırasında UI ipucu biriktiğinde | `{ hintType, ... }` (örn. `category_picker`) |
| `ResponseStart` | Son yanıt akışı başlamadan önce | `{ terminationReason }` |
| `ResponseDelta` | Yanıt metni parça parça gönderilirken | `{ text }` |
| `ResponseComplete` | Tüm yanıt gönderildikten sonra | `{ text, terminationReason }` |
| `Error` | Timeout veya workflow hatasında | `{ message }` |

Her workflow event döngüsünde `_uiHint.DrainPending(sessionId)` çağrılır ve biriken UI ipuçları anında yield edilir.

**Timeout koruması:**

```csharp
using var timeoutCts = new CancellationTokenSource(
    TimeSpan.FromSeconds(_guards.TimeoutSeconds));
```

`WorkflowGuardOptions.TimeoutSeconds` saniyesi dolduğunda `Error` eventi gönderilir ve akış sonlandırılır.

### `BuildWorkflowMessagesAsync` (özel)

Workflow'a gidecek mesaj listesini hazırlar. Sıra önemlidir:

```
1. System mesajı: ContextPipeline bağlamı (müşteri/sipariş bilgisi)
2. System mesajı: Reasoning özeti (intent, adımlar, gerekli bilgiler)
3. System mesajı: Entity hint (sorgudan çıkarılan ID'ler)
4. Konuşma geçmişi (User/Assistant mesajları)
5. [Varsa] Replan notu (admin'in "yeniden planla" talimatı)
6. User mesajı: Güncel sorgu
```

**Replan bayrağının atomik tüketimi:** `session.State.ForceReplanNextTurn`/`ReplanNote` okuma+temizleme işlemi `ConsumeForceReplanHint(session)` yardımcı metodunda `lock (session)` ile atomik yapılır. Compound query'de aynı `session` referansı birden fazla alt göreve paralel geçildiği için (`RunDecomposedAsync`/`RunDecomposedStreamingAsync` içindeki `Task.WhenAll` grupları), kilitsiz bir kontrol birden fazla eşzamanlı çağrının aynı replan ipucunu görüp tekrar tekrar enjekte etmesine yol açabilirdi — artık yalnızca **bir** çağrı bayrağı görüp tüketiyor.

### `RewriteRoutingMessageAsync` (özel)

Agent'ın ürettiği yanıt `OrderAgent:`, `PlanningAgent:` gibi iç teknik ifadeler içeriyorsa bu metod devreye girer. LLM'i `routing-rewrite-system.md` + `routing-rewrite-user.md` prompt'larıyla çağırarak mesajı kullanıcı dostu Türkçeye çevirir.

**Tetiklenme koşulu:** `WorkflowResponseExtractor.ContainsAgentRoutingMessage(result)` true döndürürse.

## Compound Query (Çoklu Görev)

Reasoning aşaması sorgunun birden fazla alt göreve bölündüğünü tespit ettiğinde (`SubTaskOrchestrator.IsCompoundQuery` → true), team tek bir workflow çalıştırmak yerine her alt görevi ayrı ayrı işler.

```
Kullanıcı: "1001'i iptal et ve iade başlat"
    │
    ├── SubTask#1 (OrderAgent): 1001 iptal
    └── SubTask#2 (ComplaintAgent): iade başlat
```

**Paralel vs Sıralı:**

`SubTaskOrchestrator.Partition` alt görevleri gruplara böler:
- **Parallel grup:** Read-only görevler (örn. iki ayrı sipariş sorgulama) → `Task.WhenAll` ile eş zamanlı çalışır
- **Sıralı grup:** Yazma işlemleri veya bağımlı görevler → biri bitmeden diğeri başlamaz

`ParallelExecutionOptions.MaxDegreeOfParallelism` ile eş zamanlı çalışacak maksimum görev sayısı sınırlandırılır.

## Trace (İzleme)

Her workflow çalışması için — **hem** `RunAsync` **hem** `RunStreamingAsync` — bir `ReasoningTrace` oluşturulur:

```csharp
var st = StartTraceState(session, query, reasoning);
// → _traceStore.StartTrace(session?.SessionId ?? "anonymous", query)
```

Olay eşlemesi `ApplyTraceEvent` içinde tanımlıdır:

- `ExecutorInvokedEvent` → `AgentVisit` başlar (ne zaman başladı), `IterationCount` artar
- `ExecutorCompletedEvent` → `AgentVisit` kapanır (ne zaman bitti, süre hesaplanır)
- `WorkflowOutputEvent` → Planning ve specialist reasoning bilgileri trace'e eklenir
- Workflow bittikten sonra `FinalizeTraceAsync` → `_traceStore.Complete(...)` çağrılır

> 0ms'lik ziyaretler trace'e eklenmez — bunlar MAF'ın iç pasif geçiş turlarıdır.
> `WellKnown.SystemExecutorPrefixes` ile eşleşen iç executor'lar da atlanır.

## Episodik Bellek ve Müşteri Profili

Workflow tamamlandıktan sonra `FinalizeTraceAsync` içinde iki opsiyonel yan etki tetiklenir, her ikisi de fire-and-forget (başarısız olursa sessizce loglanır, yanıt etkilenmez):

```
WriteEpisodicMemorySafe()       → ISemanticMemoryWriter.WriteEpisodeAsync(...)
UpdateCustomerProfileSafeAsync() → ICustomerProfileService.RecordInteractionAsync(...)
```

## Yaygın hatalar ve çözümleri

| Sorun | Olası neden | Çözüm |
|-------|-------------|-------|
| Yanıt boş geliyor | `WorkflowOutputEvent.Data` beklenen formatta değil | `WorkflowResponseExtractor.ExtractResultFromOutput` içinde yeni format case'i ekle |
| "İşlem X saniyede tamamlanamadı" | `WorkflowGuardOptions.TimeoutSeconds` çok kısa | `appsettings.json`'da `WorkflowGuards:TimeoutSeconds` değerini artır (artık `RunAsync` için de geçerli) |
| Agent adı yanıtta görünüyor | `RewriteRoutingMessageAsync` çalışmadı veya prompt başarısız | `routing-rewrite-*` prompt'larını kontrol et; LLM bağlantısını doğrula |
| Compound query tek yanıt üretiyor | `SubTaskOrchestrator.IsCompoundQuery` false dönüyor | Reasoning aşamasında `subTasks` alanının dolu geldiğini kontrol et |
