# WorkflowTraceEventProcessor

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowTraceEventProcessor.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowTraceEventProcessor`, Microsoft Agents Framework (MAF) iş akışı koşusu sırasında fırlatılan olayları (`ExecutorInvokedEvent`, `ExecutorCompletedEvent`, `AgentResponseUpdate`, `WorkflowOutputEvent`) dinleyerek; bu olayları hem adım adım [ReasoningTrace](../CustomerSupportBot.Domain/Model/ReasoningTrace.md) nesnesine yansıtan, hem de istemciye iletilecek SSE olaylarına ([StreamEvent](../CustomerSupportBot.Application/Ports/Inbound/StreamEvent.md)) dönüştüren işlemcidir.

## Hangi amaçla kullanılır`?

- **Adım Adım Gözlemlenebilirlik:** İş akışı yürütülürken hangi ajanın ne kadar süre çalıştığını (`AgentVisit`), hangi araçların hangi parametrelerle çağrıldığını (`ToolCall`) ve dönen sonuçları gerçek zamanlı kaydetmek.
- **Canlı Yanıt Filtreleme (`ResponseStreamFilter`):** `ResponseAgent`'ın token akışındaki `TERMINATE: reason=...` işaretini tamponlayarak (buffer) yakalamak ve kullanıcıya sızdırmadan temiz bir token akışı sunmak.
- **Broadcast Mesajlarını Ayırt Etme:** MAF `GroupChatHost`'un seçilmeyen ajanlara gönderdiği senkronizasyon yayınlarını (`BroadcastAsync`) `TurnToken` kontrolü ile filtreleyip yanlış tur sayımı yapılmasını önlemek.

## Sorumlulukları

- **Üstlendiği:**
  - `StartTraceState` ile yeni bir izleme durumu oluşturmak.
  - `ApplyTraceEvent` ile MAF olaylarını `TraceState` ve `StreamEvent` formatına çevirmek.
  - Ajan başlangıç/bitiş olaylarında `AgentVisit` sürelerini ve çıktılarını kaydetmek.
  - Araç çağrısı ve sonuç olaylarını `Trace.ToolCalls` listesine eklemek.
  - `ResponseStreamFilter` ile token akışını filtrelemek.

## Constructor ve Başlatma Mantığı

```csharp
public WorkflowTraceEventProcessor(
    IReasoningTraceStore traceStore,
    IApprovalContextAccessor approvalContext)
```

### Constructor İçerisinde Yapılan İşler:
- `_traceStore` (`IReasoningTraceStore`): Trace kayıtlarını başlatmak ve güncellemek üzere atanır.
- `_approvalContext` (`IApprovalContextAccessor`): Müşteri onay bağlamına `TraceId` enjekte etmek üzere atanır.

## Dahili Sınıflar

### 1. `TraceState`
- Tek bir workflow koşusunun trace durumunu taşır:
  - `Trace` (`ReasoningTrace`): Canlı trace nesnesi.
  - `ActiveVisits` (`Dictionary<string, AgentVisit>`): Ajan ziyaret kayıtları.
  - `IterationCount` (`int`): Gerçekleşen tur sayısı.
  - `ResponseStreamFilter` (`ResponseStreamFilter`): Token filtresi.
  - `ResponseStreamStarted` (`bool`): Yanıt akışının başlayıp başlamadığı.

### 2. `ResponseStreamFilter`
- `ResponseAgent`'tan akan ham token'ları `TERMINATE` işaretçisine karşı tamponlar. Parça sınırlarında bölünen kelimeleri (`TER` + `MINATE`) yakalar ve yalnızca güvenli metin parçalarını yayınlar.

## Metotlar ve İç Çalışma Mantıkları

### 1. `StartTraceState`
```csharp
public TraceState StartTraceState(AgentSession? session, string query, ReasoningResult? reasoning)
```
- **Ne işe yarar?:** Yeni bir trace kaydı açar ve `TraceState` nesnesini başlatır.
- **İç Mantığı:** `_traceStore.StartTrace` çağrılır, `_approvalContext.SetTraceId` ile bağlama enjekte edilir, varsa `reasoning` verisi eklenir ve `TraceState` döndürülür.

### 2. `ApplyTraceEvent`
```csharp
public List<StreamEvent> ApplyTraceEvent(TraceState st, WorkflowEvent evt)
```
- **Ne işe yarar?:** Gelen MAF iş akışı olayını analiz edip trace'e işler ve varsa istemciye gönderilecek `StreamEvent` listesini döner.
- **İç Mantığı:**
  - `ExecutorInvokedEvent`: `invoked.Data is TurnToken` kontrolü yapılır; seçilen ajana ait ziyaret kaydı (`AgentVisit`) başlatılır ve `AgentStarted` akış olayı üretilir.
  - `ExecutorCompletedEvent`: Ajanın çalışma süresi hesaplanır, araç çağrıları ve UI Hint'leri ayıklanır (`ExtractToolCallsFromCompleted`), `AgentCompleted` olayı üretilir.
  - `AgentResponseUpdate`: `ResponseAgent`'tan geliyorsa token'lar `ResponseStreamFilter`'dan geçirilerek `ResponseDelta` akış olayı üretilir.

## Bağımlılıklar

- [IReasoningTraceStore](../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
- [IApprovalContextAccessor](../CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.md)
- [ReasoningTrace](../CustomerSupportBot.Domain/Model/ReasoningTrace.md)
- [StreamEvent](../CustomerSupportBot.Application/Ports/Inbound/StreamEvent.md)
- `Microsoft.Agents.AI.Workflows`
