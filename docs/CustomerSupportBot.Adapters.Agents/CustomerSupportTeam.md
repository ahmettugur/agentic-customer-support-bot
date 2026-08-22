# CustomerSupportTeam

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs`
- **Tür:** `public class : IAgentTeamPort`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`CustomerSupportTeam`, hexagonal mimaride Application katmanının talep ettiği [IAgentTeamPort](../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md) portunu uygulayan ana kompozisyon köküdür (Composition Root). Kullanıcı sorgusunu alır, sorgunun tekil mi yoksa çoklu alt görev içeren birleşik (compound) bir sorgu mu olduğunu `SubTaskOrchestrator.IsCompoundQuery` ile denetler ve işi uygun koşucuya ([WorkflowRunner](WorkflowRunner.md) veya [DecomposedRunner](DecomposedRunner.md)) yönlendirir.

## Hangi amaçla kullanılır`?

Application katmanındaki servislerin (`ChatPortService`, `ReasoningService`), ajan takımının iç yapısını (MAF workflow'ları, alt ajanlar, paralel/sıralı görev dağıtımı) bilmeden hem standart metin (`RunAsync`) hem de anlık token akışı (`RunStreamingAsync`) ile müşteri destek iş akışını çalıştırması için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `IAgentTeamPort` sözleşmesini karşılamak.
  - Bağımlılıkları bir araya getirip `AgentTeamFactory`, `TurnFinalizer`, `WorkflowTraceEventProcessor`, `WorkflowMessageBuilder`, `WorkflowRunner` ve `DecomposedRunner` bileşenlerini ayağa kaldırmak.
  - Compound sorgu tespitine göre akışı `DecomposedRunner` veya `WorkflowRunner`'a devretmek.
  - `GetWorkflowDiagram()` ile MAF iş akışı diyagramını dışarıya sunmak.
- **Üstlenmediği:**
  - Ajanların prompt'larını veya araçlarını doğrudan yönetmek (bu `AgentTeamFactory` ve `Team/*` sınıflarındadır).
  - Tur sonu veri tabanı güncellemelerini tek başına yapmak (bu `TurnFinalizer`'dadır).

## Constructor ve Başlatma Mantığı

```csharp
public CustomerSupportTeam(
    IChatClient chatClient,
    IContextPipeline contextPipeline,
    IOptions<WorkflowGuardOptions> guardOptions,
    IOptions<ParallelExecutionOptions> parallelOptions,
    IReasoningTraceStore traceStore,
    IPromptRepository prompts,
    ApprovalGateService approvalGate,
    ICustomerSupportToolsService tools,
    IUiHintEmitter uiHint,
    IApprovalContextAccessor approvalContext,
    ILoggerFactory loggerFactory,
    CustomerIdentityHintBuilder identityHint,
    ISemanticMemoryWriter? semanticMemory = null,
    ICustomerProfileService? profileService = null)
```

### Constructor İçerisinde Yapılan İşler:
1. **Güvenlik Seçeneklerinin Çözülmesi:** `guardOptions.Value` okunarak `WorkflowGuardOptions` nesnesi elde edilir.
2. **Alt Bileşenlerin Örneklenmesi (Wiring):**
   - **`AgentTeamFactory`**: Ajanları ve workflow oluşturucuyu kurar (`chatClient`, `prompts`, `approvalGate`, `tools`, `guards`, `loggerFactory`).
   - **`TurnFinalizer`**: Tur sonu trace kaydetme ve yan etkileri yönetmek üzere kurulur (`traceStore`, `approvalGate`, `loggerFactory`, `semanticMemory`, `profileService`).
   - **`WorkflowTraceEventProcessor`**: MAF olaylarını trace ve stream formatına dönüştürmek üzere kurulur (`traceStore`, `approvalContext`).
   - **`WorkflowMessageBuilder`**: Workflow öncesi sistem/kullanıcı mesajlarını hazırlamak üzere kurulur (`contextPipeline`, `prompts`, `chatClient`, `loggerFactory`, `identityHint`).
3. **Koşucuların (Runners) Oluşturulması:**
   - **`_runner` (`WorkflowRunner`)**: Tekil iş akışı koşucusu örneği kurulur.
   - **`_decomposed` (`DecomposedRunner`)**: Compound sorguları alt görevlere bölüp yöneten koşucu örneği (`_runner`, `parallelOptions.Value`, `uiHint`, `approvalContext`, `finalizer`) kurulur.

## Metotlar ve İç Çalışma Mantıkları

### 1. `RunAsync`
```csharp
public Task<string> RunAsync(
    string query,
    List<ConversationMessage>? conversationHistory = null,
    AgentSession? session = null,
    ReasoningResult? reasoning = null,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Kullanıcı sorgusunu tek seferde çalıştırıp nihai asistan yanıtını döner.
- **İç Mantığı:**
  1. `SubTaskOrchestrator.IsCompoundQuery(reasoning)` kontrol edilir.
  2. Eğer `reasoning` içinde birden fazla alt görev (`SubTasks.Count > 1`) varsa `_decomposed.RunDecomposedAsync(...)` çağrılır.
  3. Tekil bir görev ise `_runner.RunAsync(...)` çağrılarak MAF workflow'u tek tur koşturulur.

### 2. `RunStreamingAsync`
```csharp
public IAsyncEnumerable<StreamEvent> RunStreamingAsync(
    string query,
    List<ConversationMessage>? conversationHistory = null,
    AgentSession? session = null,
    ReasoningResult? reasoning = null,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Kullanıcı sorgusunu çalıştırırken ara durumları, araç çağrılarını ve LLM token'larını gerçek zamanlı SSE olarak stream eder.
- **İç Mantığı:**
  1. `SubTaskOrchestrator.IsCompoundQuery(reasoning)` ile compound kontrolü yapılır.
  2. Bileşik sorgularda `_decomposed.RunDecomposedStreamingAsync(...)` çağrılarak sıralı/paralel gruplar canlı stream edilir.
  3. Tekil sorgularda `_runner.RunStreamingAsync(...)` çağrılarak anlık `StreamEvent` akışı üretilir.

### 3. `GetWorkflowDiagram`
```csharp
public string GetWorkflowDiagram()
```
- **Ne işe yarar?:** Çoklu ajan takımının MAF workflow topolojisini Mermaid formatında diyagram olarak döndürür.
- **İç Mantığı:** `_runner.GetWorkflowDiagram()` çağrısını yönlendirir.

## Bağımlılıklar

- [IAgentTeamPort](../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md)
- [WorkflowRunner](WorkflowRunner.md)
- [DecomposedRunner](DecomposedRunner.md)
- [AgentTeamFactory](AgentTeamFactory.md)
- [ApprovalGateService](ApprovalGateService.md)
- [TurnFinalizer](TurnFinalizer.md)
- [WorkflowTraceEventProcessor](WorkflowTraceEventProcessor.md)
- [WorkflowMessageBuilder](WorkflowMessageBuilder.md)
