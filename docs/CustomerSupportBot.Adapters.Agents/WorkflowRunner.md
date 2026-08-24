# WorkflowRunner

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowRunner.cs`
- **Tür:** `internal sealed class : IWorkflowRunner`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowRunner`, tekil bir kullanıcı sorgusunu veya [DecomposedRunner](DecomposedRunner.md) tarafından ayrıştırılmış tek bir alt görevi Microsoft Agents Framework (MAF) iş akışı (`AgentGroupWorkflow`) üzerinde koşturan, olay döngüsünü (event loop) yürüten ve anlık token/olay akışını (`StreamEvent`) yöneten temel yürütme motorudur.

## Hangi amaçla kullanılır`?

Workflow mesajlarını [WorkflowMessageBuilder](WorkflowMessageBuilder.md) ile oluşturmak, [AgentTeamFactory](AgentTeamFactory.md) üzerinden taze bir iş akışı başlatmak, `WorkflowTraceEventProcessor` ile adımları izlemek, anormal sonlanma veya zaman aşımı durumlarını yönetmek ve tur sonunda [TurnFinalizer](TurnFinalizer.md) ile yan etkileri tetiklemek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `IWorkflowRunner` arayüzünü uygulamak.
  - `RunAsync` ile tam metin yanıtı üretmek.
  - `RunStreamingAsync` ile token ve ara olay akışını (`StreamEvent`) yayımlamak.
  - Zaman aşımı (`TimeoutSeconds`) ve iptal mekanizmalarını (`CancellationTokenSource`) yönetmek.
  - MAF olay döngüsünü (`InProcessExecution.RunStreamingAsync`) tüketmek ve `RequestInfoEvent` gibi süperadım duraklamalarını karşılamak.
  - MAF iş akışı diyagramını (`ToMermaidString()`) üretmek.
- **Üstlenmediği:**
  - RAG bağlamı ve prompt hazırlamak (bu [WorkflowMessageBuilder](WorkflowMessageBuilder.md) sınıfındadır).
  - Olayların trace nesnesine çevrilmesi (bu [WorkflowTraceEventProcessor](WorkflowTraceEventProcessor.md) sınıfındadır).
  - Tur sonu veri tabanı güncellemeleri (bu [TurnFinalizer](TurnFinalizer.md) sınıfındadır).

## Constructor ve Başlatma Mantığı

```csharp
public WorkflowRunner(
    AgentTeamFactory factory,
    TurnFinalizer finalizer,
    WorkflowGuardOptions guards,
    IReasoningTraceStore traceStore,
    ApprovalGateService approvalGate,
    IUiHintEmitter uiHint,
    ILoggerFactory loggerFactory,
    WorkflowTraceEventProcessor traceProcessor,
    WorkflowMessageBuilder messageBuilder)
```

### Constructor İçerisinde Yapılan İşler:
- Fabrika (`_factory`), sonlandırıcı (`_finalizer`), güvenlik kuralları (`_guards`), izleme ambarı (`_traceStore`), onay kapısı (`_approvalGate`), UI ipucu yayıcı (`_uiHint`), log fabrikası (`_loggerFactory`), trace işlemci (`_traceProcessor`) ve mesaj derleyici (`_messageBuilder`) bağımlılıkları özel alanlara atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `RunAsync`
```csharp
public async Task<string> RunAsync(
    string query,
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ReasoningResult? reasoning,
    CancellationToken ct)
```
- **Ne işe yarar?:** Tekil bir sorguyu MAF iş akışında koşturur ve nihai yanıt metnini döndürür.
- **İç Mantığı:**
  1. `_guards.TimeoutSeconds` süresiyle zaman aşımı `CancellationTokenSource`'u oluşturulur ve `ct` ile bağlanır (`effectiveCt`).
  2. `_messageBuilder.BuildWorkflowMessagesAsync` çağrılarak sistem, kullanıcı, ID ipucu ve reasoning mesajları derlenir.
  3. `_traceProcessor.StartTraceState` ile yeni bir `TraceState` başlatılır; tahmini token sayısı ve bağlam parçaları trace'e yazılır.
  4. `_factory.CreateWorkflow(reasoning?.ConstrainedTargetAgent)` ile taze iş akışı oluşturulur.
  5. `InProcessExecution.RunStreamingAsync` başlatılır ve ilk tur tetikleme belirteci (`TurnToken(emitEvents: true)`) gönderilir.
  6. `EnumerateWorkflowEventsSafely` döngüsü ile workflow olayları dinlenir; `RequestInfoEvent` veya `WorkflowErrorEvent` durumları yönetilir, her olay `_traceProcessor.ApplyTraceEvent` ile işlenir.

     > 🐞 **`RequestInfoEvent` işleyicisi şu an tetiklenmiyor:** `HandleRequestInfoEventAsync` →
     > `ApprovalGateService.RequestApprovalAsync` köprüsü koddadır ama bloklayan/senkron eski
     > onay modeline aitti (admin karar verene kadar await eder). Yan etkili 4 tool (sipariş,
     > iptal, iade, şikayet) artık `ApprovalRequiredAIFunction` ile SARILMIYOR — bu yüzden hiçbir
     > zaman bir `RequestInfoEvent` üretmiyorlar, bu kod yolu şu an ölü değil ama **kullanılmayan**
     > bir güvenlik ağı. Güncel bloklamayan akış için bkz.
     > [ApprovalGateService.md](ApprovalGateService.md#6-executewithapprovalgateasync-private).
  7. Döngü bittiğinde `FinalizeAbnormalTerminationAsync` ile anormal sonlanma (Timeout, Cancelled, Error) denetlenir.
  8. `BuildFinalResultAsync` çağrılarak sonuç metni oluşturulur ve `_finalizer.FinalizeTurnAsync` ile tur kapatılır.

### 2. `RunStreamingAsync`
```csharp
public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
    string query,
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ReasoningResult? reasoning,
    [EnumeratorCancellation] CancellationToken ct)
```
- **Ne işe yarar?:** İş akışını çalıştırırken ara düşünceleri, araç çağrılarını ve LLM token'larını `StreamEvent` olarak anlık iletir.
- **İç Mantığı:**
  1. Mesajlar hazırlanır ve `TraceState` başlatılır.
  2. MAF olay döngüsü başlatılır; gelen her `ExecutorInvokedEvent`, `ExecutorCompletedEvent` ve `AgentResponseUpdate` olayı `_traceProcessor` üzerinden SSE formatında yield edilir.
  3. ResponseAgent'ın canlı token akışı `ResponseStreamFilter` ile filtrelenerek `ResponseDelta` olarak yayınlanır.
  4. Akış tamamlandığında `response_complete` olayı ile nihai metin ve trace ID istemciye sunulur.

### 3. `GetWorkflowDiagram`
```csharp
public string GetWorkflowDiagram()
```
- **Ne işe yarar?:** `_factory.CreateWorkflow().ToMermaidString()` çağrısıyla iş akışının Mermaid diyagramını üretir.

### 4. `EnumerateWorkflowEventsSafely` (Private)
- **Ne işe yarar?:** MAF olay akışını dinlerken fırlatılabilecek bağlantı veya zaman aşımı istisnalarını güvenli şekilde yakalar ve döngüyü kontrollü şekilde sonlandırır. Yakalanan **gerçek `Exception` nesnesini** döner (`(WorkflowEvent? evt, Exception? error)`) — eskiden yalnızca `ex.Message` taşınıyordu, bu yüzden çağıranlar bunu hiçbir zaman [ExceptionTranslator](ExceptionTranslator.md)'dan geçiremiyordu ve `RunStreamingAsync` bu ham metni doğrudan istemciye gönderiyordu (bkz. `ExceptionTranslator.md`'deki 🐞 notu).

  `RunAsync`/`RunStreamingAsync`'teki `workflowError` değişkeni ve `FinalizeAbnormalTerminationAsync`'in döndürdüğü `RunOutcome.Error` de aynı sebeple `string?` yerine `Exception?` taşır. Hata yolunda: **trace'e** (`_traceStore.Complete(..., error: ...)`) ham `exception.Message` yazılır (admin/debug için); **istemciye** (`RunAsync`'te `throw`, `RunStreamingAsync`'te `yield return StreamEvent.Error`) her zaman `ExceptionTranslator.Translate(ex, sabitContext).Message` gönderilir.

### 5. `BuildFinalResultAsync` (Private)
- **Ne işe yarar?:** İş akışının çıktısını [WorkflowResponseExtractor](WorkflowResponseExtractor.md) ile analiz eder, uzman JSON'larını ayıklar ve kullanıcıya gösterilecek temiz metni derler.

## Bağımlılıklar

- `Microsoft.Agents.AI.Workflows`
- [AgentTeamFactory](AgentTeamFactory.md)
- [TurnFinalizer](TurnFinalizer.md)
- [WorkflowTraceEventProcessor](WorkflowTraceEventProcessor.md)
- [WorkflowMessageBuilder](WorkflowMessageBuilder.md)
- [ApprovalGateService](ApprovalGateService.md)
- [ExceptionTranslator](ExceptionTranslator.md)
