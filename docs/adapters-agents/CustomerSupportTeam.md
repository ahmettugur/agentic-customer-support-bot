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
| 2 | ProductInquiryAgent | `WellKnown.AgentNames.ProductInquiry` | `product_inquiry_tool` |
| 3 | OrderAgent | `WellKnown.AgentNames.Order` | `order_placement_tool` (HITL) + `order_status_tool` + `get_last_order_tool` + `get_all_orders_tool` |
| 4 | ComplaintAgent | `WellKnown.AgentNames.Complaint` | `complaint_registration_tool` (HITL) |
| 5 | HumanHandoffAgent | `WellKnown.AgentNames.HumanHandoff` | `human_handoff_tool` |
| 6 | ResponseAgent | `WellKnown.AgentNames.Response` | Yok (yalnızca biçimlendirme) |

> **HITL nedir?** `order_placement_tool` ve `complaint_registration_tool` direkt çalışmaz; önce `ApprovalGateService` üzerinden admin onayı bekler. Onay gelmezse tool reddedilir.

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
    ILoggerFactory loggerFactory,
    ISemanticMemoryWriter? semanticMemory = null, // Opsiyonel: episodik bellek (semantic search)
    ICustomerProfileService? profileService = null) // Opsiyonel: müşteri profili güncelleyici
```

> `ISemanticMemoryWriter` ve `ICustomerProfileService` opsiyoneldir — null gelirse özellik sessizce atlanır.

## Ana metodlar

### `RunAsync` (Non-streaming)

```csharp
public async Task<string> RunAsync(
    string query,
    List<ConversationMessage>? conversationHistory = null,
    AgentSession? session = null,
    ReasoningResult? reasoning = null)
```

1. Compound query mi? → `RunDecomposedAsync` çağrılır (aşağıda açıklandı).
2. Workflow mesajları oluşturulur (`BuildWorkflowMessagesAsync`).
3. Yeni bir `Workflow` + `InProcessExecution` başlatılır.
4. `WorkflowOutputEvent` yakalanır, sonuç çıkarılır.
5. `TERMINATE` marker'ları ve teknik JSON blokları temizlenir.
6. Sonuç agent routing mesajı içeriyorsa `RewriteRoutingMessageAsync` ile kullanıcı dostu hale getirilir.

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
| `ResponseStart` | Son yanıt akışı başlamadan önce | `{ terminationReason }` |
| `ResponseDelta` | Yanıt metni parça parça gönderilirken | `{ text }` |
| `ResponseComplete` | Tüm yanıt gönderildikten sonra | `{ text, terminationReason }` |
| `Error` | Timeout veya workflow hatasında | `{ message }` |

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

### `RewriteRoutingMessageAsync` (özel)

Agent'ın ürettiği yanıt `OrderAgent:`, `PlanningAgent:` gibi iç teknik ifadeler içeriyorsa bu metod devreye girer. LLM'i `routing-rewrite-system.md` + `routing-rewrite-user.md` prompt'larıyla çağırarak mesajı kullanıcı dostu Türkçeye çevirir.

**Tetiklenme koşulu:** `WorkflowResponseExtractor.ContainsAgentRoutingMessage(result)` true döndürürse.

## Compound Query (Çoklu Görev)

Reasoning aşaması sorgunun birden fazla alt göreve bölündüğünü tespit ettiğinde (`SubTaskOrchestrator.IsCompoundQuery` → true), team tek bir workflow çalıştırmak yerine her alt görevi ayrı ayrı işler.

```
Kullanıcı: "ORD-1001'i iptal et ve iade başlat"
    │
    ├── SubTask#1 (OrderAgent): ORD-1001 iptal
    └── SubTask#2 (ComplaintAgent): iade başlat
```

**Paralel vs Sıralı:**

`SubTaskOrchestrator.Partition` alt görevleri gruplara böler:
- **Parallel grup:** Read-only görevler (örn. iki ayrı sipariş sorgulama) → `Task.WhenAll` ile eş zamanlı çalışır
- **Sıralı grup:** Yazma işlemleri veya bağımlı görevler → biri bitmeden diğeri başlamaz

`ParallelExecutionOptions.MaxDegreeOfParallelism` ile eş zamanlı çalışacak maksimum görev sayısı sınırlandırılır.

## Trace (İzleme)

`RunStreamingAsync` içinde her workflow çalışması için bir `ReasoningTrace` oluşturulur:

```csharp
var trace = _traceStore.StartTrace(session?.SessionId ?? "anonymous", query);
```

- `ExecutorInvokedEvent` → `AgentVisit` başlar (ne zaman başladı)
- `ExecutorCompletedEvent` → `AgentVisit` kapanır (ne zaman bitti, süre hesaplanır)
- `WorkflowOutputEvent` → Planning ve specialist reasoning bilgileri trace'e eklenir
- Workflow bittikten sonra `_traceStore.Complete(...)` çağrılır

> 0ms'lik ziyaretler trace'e eklenmez — bunlar MAF'ın iç pasif geçiş turlarıdır.

## Episodik Bellek ve Müşteri Profili

Workflow tamamlandıktan sonra iki opsiyonel yan etki tetiklenir, her ikisi de fire-and-forget (başarısız olursa sessizce loglanır, yanıt etkilenmez):

```
WriteEpisodicMemorySafe()       → ISemanticMemoryWriter.WriteEpisodeAsync(...)
UpdateCustomerProfileSafeAsync() → ICustomerProfileService.RecordInteractionAsync(...)
```

## Yaygın hatalar ve çözümleri

| Sorun | Olası neden | Çözüm |
|-------|-------------|-------|
| Yanıt boş geliyor | `WorkflowOutputEvent.Data` beklenen formatta değil | `WorkflowResponseExtractor.ExtractResultFromOutput` içinde yeni format case'i ekle |
| "İşlem X saniyede tamamlanamadı" | `WorkflowGuardOptions.TimeoutSeconds` çok kısa | `appsettings.json`'da `Workflow:TimeoutSeconds` değerini artır |
| Agent adı yanıtta görünüyor | `RewriteRoutingMessageAsync` çalışmadı veya prompt başarısız | `routing-rewrite-*` prompt'larını kontrol et; LLM bağlantısını doğrula |
| Compound query tek yanıt üretiyor | `SubTaskOrchestrator.IsCompoundQuery` false dönüyor | Reasoning aşamasında `subTasks` alanının dolu geldiğini kontrol et |
