# DecomposedRunner

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/DecomposedRunner.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`DecomposedRunner`, birleşik/karmaşık (compound) kullanıcı sorgularında [ReasoningResult](../CustomerSupportBot.Domain/Model/ReasoningResult.md) içerisindeki `SubTasks` planını alarak; bağımsız/yan-etkisiz görevleri paralel (`SemaphoreSlim` ve `Task.WhenAll`), bağımlı/yan-etkili görevleri ise sıralı gruplar halinde [IWorkflowRunner](IWorkflowRunner.md) üzerinden koşturan ve sonuçları `sub.Order` sırasına göre birleştiren orkestrasyon motorudur.

## Hangi amaçla kullanılır`?

- **Paralel Yürütme ve Düşük Gecikme:** Birbirinden bağımsız sorguları (örneğin "ayakkabı fiyatları nedir ve kargo takip nasıl yapılır?") eşzamanlı çalıştırarak yanıt süresini minimuma indirmek.
- **Sıralı Güvenlik ve Bağımlılık Zinciri:** Yan etkili işlemleri (sipariş iptali, iade kaydı vb.) sırayla koşturmak ve önceki alt görevin yanıtını sonraki alt görevin geçmişine (`BuildSubTaskHistory`) aktarmak.
- **Canlı ve Kesintisiz Token Akışı:** Sıralı alt görevlerin token akışlarını [TrimmingDeltaStreamer](TrimmingDeltaStreamer.md) ile temizleyerek kullanıcıya gerçek zamanlı sunmak.
- **Tekil Tur Yan Etkisi Garantisi:** N adet alt görev koşturulduğunda episodik bellek ve müşteri profili sayaçlarının N kez değil, aggregate birleşimle yalnızca 1 kez güncellenmesini sağlamak ([TurnFinalizer.FinalizeAggregateTurnAsync](TurnFinalizer.md)).

## Sorumlulukları

- **Üstlendiği:**
  - `SubTaskOrchestrator.ValidateExecutionPlan` ile yürütme planını doğrulamak.
  - `SubTaskOrchestrator.Partition` ile görevleri paralel ve sıralı gruplara ayırmak.
  - `MaxDegreeOfParallelism` sınırıyla `SemaphoreSlim` kullanarak kontrollü paralellik sağlamak.
  - `RunDecomposedAsync` ve `RunDecomposedStreamingAsync` metotlarını sunmak.
  - Tur sonunda `TurnFinalizer.FinalizeAggregateTurnAsync` ile aggregate yan etkileri tetiklemek.
- **Üstlenmediği:**
  - Tek bir alt görevin MAF workflow'unu kurup koşturması (bu [IWorkflowRunner](IWorkflowRunner.md) / [WorkflowRunner](WorkflowRunner.md) sınıfındadır).

## Constructor ve Başlatma Mantığı

```csharp
public DecomposedRunner(
    IWorkflowRunner runner,
    ParallelExecutionOptions parallelOptions,
    IUiHintEmitter uiHint,
    IApprovalContextAccessor approvalContext,
    TurnFinalizer finalizer)
```

### Constructor İçerisinde Yapılan İşler:
- `_runner` (`IWorkflowRunner`): Tekil alt görev koşucusu soyutlaması atanır.
- `_parallelOptions` (`ParallelExecutionOptions`): Zaman aşımı, paralellik derecesi ve gruplama kuralları saklanır.
- `_uiHint` (`IUiHintEmitter`): Alt görev ilerleme olaylarını UI'a iletmek üzere saklanır.
- `_approvalContext` (`IApprovalContextAccessor`): Müşteri kimliği ve onay bağlamı yöneticisi saklanır.
- `_finalizer` (`TurnFinalizer`): Aggregate tur kapanışını yapmak üzere saklanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `RunDecomposedAsync`
```csharp
public async Task<string> RunDecomposedAsync(
    string query,
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ReasoningResult reasoning,
    CancellationToken ct)
```
- **Ne işe yarar?:** Compound sorgudaki tüm alt görevleri paralel/sıralı gruplar halinde çalıştırıp tek bir birleşik metin yanıtı döner.
- **İç Mantığı:**
  1. `SubTaskOrchestrator.ValidateExecutionPlan` ile yürütme planı oluşturulur ve `Partition` ile gruplara ayrılır.
  2. `ParallelExecutionOptions.TimeoutSeconds` süresiyle zaman aşımı CTS'i oluşturulur.
  3. Gruplar sırayla işlenir:
     - **Paralel Grup:** `SemaphoreSlim` eşliğinde `group.Items.Select(async sub => ...)` görevleri `Task.WhenAll` ile eşzamanlı çalıştırılır.
     - **Sıralı Grup:** Görevler `foreach` ile sırayla koşturulur, her tamamlanan görev `completed` sözlüğüne yazılır.
  4. Toplanan sonuçlar `sub.Order` sırasına göre sıralanır ve `SubTaskOrchestrator.AggregateSubTaskResults` ile birleştirilir.
  5. `_finalizer.FinalizeAggregateTurnAsync` çağrılarak birleşik tur trace'i ve episodik hafıza tek seferde yazılır.

### 2. `RunDecomposedStreamingAsync`
```csharp
public async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
    string query,
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ReasoningResult reasoning,
    [EnumeratorCancellation] CancellationToken ct)
```
- **Ne işe yarar?:** Compound sorgudaki alt görevlerin başlangıç, düşünce, araç ve token akışlarını gerçek zamanlı SSE olarak stream eder.
- **İç Mantığı:**
  1. Baştan tek bir `response_start` (`decomposed: true`, `subTaskCount`) yayınlanır — akışın tek-sorgu/compound ayrımı tüketici (Blazor) için bu bayrakla yapılır; Blazor normalde `response_start`'ta ajan çiplerini mühürler ama `decomposed:true` iken bunu `response_complete`'e erteler (aksi halde "SubTask#N" ilerleme çipleri erken silinirdi).
  2. Her alt görev için `Agent` tipinde bir olay `status: "running"` ile başlar, tamamlandığında `status: "done"` (hata durumunda `"failed"`) ile kapanır — ayrı bir "SubTaskStarted/Completed" olay tipi YOKTUR, hepsi aynı `StreamEventTypes.Agent` olayının `status` alanıyla ayırt edilir.
  3. **Sıralı gruplar:** alt görevin GERÇEK LLM token akışı [TrimmingDeltaStreamer](TrimmingDeltaStreamer.md) ile temizlenip `response_delta` olarak canlı yayınlanır; alt görevler arası `SubTaskOrchestrator.ResultSeparator` eklenir.
  4. **Paralel gruplar:** `Task.WhenAll` tamamlanana kadar hiçbir token akmaz; tüm grup bitince sonuçlar `sub.Order` sırasına göre toplu `response_delta` olarak yayınlanır (araya girmesin diye).
  5. En sonda `_finalizer.FinalizeAggregateTurnAsync` çağrılır ve tüm görevlerin birleşimi tek bir `response_complete` olayı (`Decomposed: true`, `SubTaskCount`) ile istemciye sunulur.

### 4. `EnumerateSubTaskEventsSafely` (Private Static)
- **Ne işe yarar?:** Bir alt görevin `IAsyncEnumerable<StreamEvent>` akışını, `MoveNextAsync` sırasında fırlayabilecek istisnaları (iptal veya başka bir hata) yutup `(event, error)` çiftine çeviren güvenli sarmalayıcıdır — akışın ortasında bir istisna, tüm `RunDecomposedStreamingAsync` iterator'ünü `yield` zincirinin dışında çökertmesin diye.

### 3. `BuildSubTaskHistory` (Private Static)
- **Ne işe yarar?:** Bir alt görev çalıştırılmadan önce, kendisinden önce tamamlanan kardeş alt görevlerin soru-cevaplarını sohbet geçmişine asistan mesajı olarak ekler; böylece birbirini takip eden adımlarda veri sürekliliği sağlanır.

## Bağımlılıklar

- [IWorkflowRunner](IWorkflowRunner.md)
- [TurnFinalizer](TurnFinalizer.md)
- [TrimmingDeltaStreamer](TrimmingDeltaStreamer.md)
- [ParallelExecutionOptions](../CustomerSupportBot.Application/Ports/Outbound/ParallelExecutionOptions.md)
- [ReasoningResult](../CustomerSupportBot.Domain/Model/ReasoningResult.md)
