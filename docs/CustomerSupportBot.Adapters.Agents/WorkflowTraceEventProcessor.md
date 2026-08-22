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
  - Ajan başlangıç/bitiş olaylarında `AgentVisit` sürelerini kaydetmek; ara durumdan (`ExecutorCompletedEvent`) ve final durumdan (`WorkflowOutputEvent`) plan/uzman-reasoning verisini çıkarıp trace'e işlemek.
  - `ResponseStreamFilter` ile token akışını filtrelemek.
  - **Tek yönlü kod garantileri** (LLM'in reflection JSON'unu yanlış/eksik üretmesine karşı): `EnsureHumanHandoffEscalation` ve `EnsureSideEffectToolCompletion`.
- **Üstlenmediği:**
  - Bir ayrı `Trace.ToolCalls` listesi tutmak — böyle bir alan/koleksiyon YOKTUR; araç sonucu bilgisi yalnızca `EnsureSideEffectToolCompletion` içinde geçici olarak (`FunctionCallContent`/`FunctionResultContent` üzerinden) okunur, kalıcı olarak trace'e ayrı yazılmaz.

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
- **Ne işe yarar?:** Gelen MAF iş akışı olayını analiz edip trace'e işler ve varsa istemciye gönderilecek `StreamEvent` listesini döner (boş liste dönebilir — `RunAsync` bunu yok sayar, sadece `RunStreamingAsync` kullanır).
- **İç Mantığı (event tipine göre):**
  - **`ExecutorInvokedEvent`:** İç sistem executor'ları (`WorkflowResponseExtractor.IsInternalWorkflowExecutor`) atlanır. `invoked.Data is not TurnToken` ise de atlanır — `GroupChatHost`, seçilmeyen tüm ajanlara geçmiş senkronizasyonu için (`BroadcastAsync`) düz `ChatMessage` listesiyle de Invoked/Completed çifti üretir ama ajan GERÇEKTE çalışmaz; framework'ün tek gerçek-tur sinyali `TurnToken`'dır. Gerçek bir tur ise `IterationCount` artırılır, `AgentVisit` başlatılır, `IApprovalContextAccessor.SetCurrentAgent` ile ambient ajan adı set edilir (tool çağrılarının hangi ajana ait olduğunu bilmek için) ve `StreamEventTypes.Agent` (`status: "running"`) döner.
  - **`ExecutorCompletedEvent`:** `ActiveVisits`'te kaydı olmayan (yani Invoked aşamasında zaten atlanmış broadcast) tamamlanmalar yok sayılır. Ziyaret trace'e taşınır (`ActiveVisits` → `Trace.AgentVisits`). `completed.Data` bir `ChatMessage` koleksiyonuysa (ara durum) `WorkflowResponseExtractor.ExtractPlanning`/`ExtractSpecialistReasonings` ile plan/reasoning ÇIKARILIR — workflow timeout/hata ile hiç tamamlanmasa bile o ana kadarki reflection'lar (ör. `needs_escalation`) kaybolmasın diye. Tamamlanan ajan `HumanHandoffAgent`/`OrderAgent`/`ComplaintAgent` ise sırasıyla `EnsureHumanHandoffEscalation`/`EnsureSideEffectToolCompletion` çağrılır. `StreamEventTypes.Agent` (`status: "done"`) döner.
  - **`AgentResponseUpdateEvent`:** Yalnızca `ResponseAgent`'tan gelen güncellemeler işlenir (diğer ajanların çıktısı yapılandırılmış JSON'dur, kullanıcıya asla akıtılmaz). Ham metin `ResponseStreamFilter.Feed` ile `TERMINATE` işaretine karşı süzülür; ilk güvenli metin geldiğinde önce `StreamEventTypes.ResponseStart` sonra `StreamEventTypes.ResponseDelta` döner.
  - **`WorkflowOutputEvent`:** Nihai sonuç metni ve plan/reasoning `WorkflowResponseExtractor`'ın `...FromOutput` metotlarıyla çıkarılır; son güvence olarak `EnsureHumanHandoffEscalation`/`EnsureSideEffectToolCompletion` burada da (tüm mesajlar üzerinde) tekrar çalıştırılır — `ExecutorCompletedEvent` aşamasında bir sebeple kaçırılmış olabilir diye. `NoEvents` döner (sonuç metni ayrıca stream edilmez, `WorkflowRunner` `st.Result`'ı okur).
  - Diğer event tipleri: `NoEvents`.

### 3. `MergeSpecialistReasonings` (Private Static)
```csharp
private static void MergeSpecialistReasonings(ReasoningTrace trace, List<SpecialistReasoning> incoming)
```
- **Ne işe yarar?:** Aynı ajanın hem ara durumda hem final `WorkflowOutputEvent`'te görülebilen reasoning'ini ajan başına TEK (en güncel) kayıt olacak şekilde birleştirir — dedup olmasaydı eskalasyon aday listesi mükerrer kayıt üretirdi.

### 4. `EnsureHumanHandoffEscalation` (Internal Static)
```csharp
internal static void EnsureHumanHandoffEscalation(IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings)
```
- **Ne işe yarar?:** `human_handoff_tool` gerçekten çağrılmışsa (`FunctionCallContent` üzerinden deterministik tespit, bkz. `WorkflowResponseExtractor.ContainsHumanHandoffToolCall`) ilgili `SpecialistReasoning.PostToolReflection.Status`'ü `NeedsEscalation`'a **koddan** zorlar — LLM'in bu alanı doğru işaretlemesine güvenmez.
- **Neden gerekli:** `EscalationPolicyService.ProcessPendingEscalationsAsync` SADECE bu status alanına bakarak admin panelinde eskalasyon kaydı açar. Model reflection JSON'unu yanlış üretirse (unutursa) kullanıcı "temsilciye bağlanacaksınız" mesajı alır ama panelde HİÇ kayıt açılmazdı — sessiz bir başarısızlıktı.
- **Bilinçli tasarım kararı — tek yönlü garanti:** Yalnızca "tool çağrıldıysa eskale et" yönünde zorlanır; tool gereksiz çağrılıp reflection "aslında gerek yok" dese bile kayıt yine de açılır (kaçırılan eskalasyonun maliyeti fazladan eskalasyondan yüksek, panelde dismiss yolu var). Var olan diğer alanlar (`PreToolCheck`, `ResultConfidence`, `ResultNotes`, `MissingContext`) korunur — `MissingContext` fonksiyoneldir, `EscalationPolicyService` doğrudan `EscalationRequest`'e kopyalar.
- **Bilinen kapsam dışı köşe:** `HumanHandoffAgent` turu tamamlanmadan (uçuş hâlindeyken) workflow timeout'a takılırsa `FunctionCallContent` hiç oluşmaz, garanti devreye giremez.

### 5. `EnsureSideEffectToolCompletion` (Internal Static)
```csharp
internal static void EnsureSideEffectToolCompletion(
    IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings,
    string agentName, IReadOnlySet<string> toolNames)
```
- **Ne işe yarar?:** `EnsureHumanHandoffEscalation` ile aynı desenin TERS yönü — bir yan-etkili tool (`order_placement_tool`, `order_cancel_tool`, `return_request_tool`, `complaint_registration_tool`; `WellKnown.SideEffectToolsOf(agentName)`'den okunur) `ToolResult.Success=true` ile sonuçlanmışsa, ilgili ajanın reflection'ını **koddan** `Done` (veya onay bekliyorsa `PendingApproval`, `ToolResult.PendingApproval` alanına bakılarak) durumuna zorlar.
- **Neden gerekli:** LLM reflection'ı başarılı bir işlemi yanlışlıkla `needs_followup`/`failed` işaretlerse müşteri "işlem yapılamadı" gibi yanlış-negatif bir yanıt alabilir ya da gereksiz bir replan turu tetiklenebilir.
- **`Done` ile `PendingApproval` ayrımı neden var:** Bloklamayan HITL modelinde `ToolResult.Success=true` iki farklı gerçek anlama gelebilir — iş GERÇEKTEN tamamlandı, veya sadece onay kuyruğuna eklendi (bkz. [ApprovalGateService](ApprovalGateService.md)). İkisi karıştırılırsa müşteri henüz gerçekleşmemiş bir işlemi "oldu" sanır.
- **Bilinçli asimetri:** Yalnızca BAŞARI yönünde düzeltilir; tool başarısız olduysa veya sonucu belirlenemiyorsa hiç dokunulmaz — başarısızlığı "done" yapmak, gerçekleşmemiş bir işlemi müşteriye onaylamak gibi çok daha riskli bir hata olurdu.
- Yardımcı: `TryGetToolOutcome(object? raw)` — `AIFunctionFactory` sonucunu bazen ham `ToolResult`, bazen (serileştirme yoluna bağlı) `JsonElement` olarak taşıdığı için ikisini tek yerde `(bool? Success, bool PendingApproval)`'a normalize eder.

### 6. `ResolveApprovalJustification` (Public Static)
```csharp
public static string ResolveApprovalJustification(TraceState st)
```
- **Ne işe yarar?:** Admin panelinde "bu tool neden çağrılıyor" sorusunun cevabı olarak gösterilecek gerekçeyi seçer: önce `Trace.Planning.Rationale` (PlanningAgent'ın routing gerekçesi — uzman turundan ÖNCE tamamlandığı için o an zaten trace'te mevcuttur), yoksa `Trace.Reasoning.Rationale`, o da yoksa boş string (çağıran `ApprovalGateService` jenerik şablona düşer). Uzmanın kendi `preToolCheck.reasoning`'i kullanılmaz çünkü o alan tool ÇALIŞTIKTAN sonra üretilen final JSON'un parçasıdır — onay ise tool çalışmadan önce tetiklenir.

## Bağımlılıklar

- [IReasoningTraceStore](../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
- [IApprovalContextAccessor](../CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.md)
- [ReasoningTrace](../CustomerSupportBot.Domain/Model/ReasoningTrace.md)
- [StreamEvent](../CustomerSupportBot.Application/Ports/Inbound/StreamEvent.md)
- [WorkflowResponseExtractor](WorkflowResponseExtractor.md)
- `Microsoft.Agents.AI.Workflows`
