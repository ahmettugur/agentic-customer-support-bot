# CustomerSupportTeam

**Dosya:** `CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs`
**Implements:** `IAgentTeamPort` (Application katmanı portu)
**Yaşam döngüsü:** Singleton

## Ne işe yarar?

`IAgentTeamPort`'un implementasyonu ve katmanın **kompozisyon kökü**dür. Kendisi hiçbir workflow/agent mantığı içermez — yalnızca `AgentTeamFactory`, `TurnFinalizer`, `WorkflowRunner`, `DecomposedRunner` nesnelerini kurar ve gelen isteği compound/single query ayrımına göre doğru koşucuya yönlendirir.

> **Mimari not:** Bu sınıf eskiden (ajan tanımları, workflow kurulumu, event işleme, HITL köprüsü dahil) tüm orkestrasyon mantığını tek başına barındırıyordu. Refactor sonrası bu sorumluluklar `AgentTeamFactory` (ajan/workflow kurulumu), `WorkflowRunner` (tek-sorgu koşusu + trace + streaming), `DecomposedRunner` (compound query orkestrasyonu) ve `TurnFinalizer`'a (tur-sonu yan etkileri) bölündü — bu sınıf yalnızca ince bir yönlendirici olarak kaldı.

## Hangi amaçla kullanılır?

Application katmanındaki `ChatPortService` (ve `EvaluationRunner`, `ReplanService` gibi diğer `IAgentTeamPort` tüketicileri), bir kullanıcı mesajını işlemek için bu sınıfın `RunAsync`/`RunStreamingAsync` metotlarını çağırır.

## Sorumlulukları

- Constructor'da `AgentTeamFactory`, `TurnFinalizer`, `WorkflowRunner`, `DecomposedRunner` nesnelerini kurmak (bkz. aşağıdaki bağımlılık grafiği).
- `RunAsync`/`RunStreamingAsync` çağrıldığında `SubTaskOrchestrator.IsCompoundQuery(reasoning)` ile compound/single query ayrımını yapıp isteği `DecomposedRunner` veya `WorkflowRunner`'a yönlendirmek.
- `GetWorkflowDiagram()` çağrısını `WorkflowRunner.GetWorkflowDiagram()`'a devretmek (admin panelindeki Mermaid diyagramı endpoint'i için).

**Üstlenmediği işler:** Her türlü ajan/workflow/routing/HITL/trace mantığı — hepsi kurduğu alt bileşenlere ait (bkz. [WorkflowRunner.md](WorkflowRunner.md), [AgentTeamFactory.md](AgentTeamFactory.md), [DecomposedRunner.md](DecomposedRunner.md), [TurnFinalizer.md](TurnFinalizer.md)).

## Diğer katman ve bileşenlerle ilişkileri

**Implements:** `CustomerSupportBot.Application.Ports.Outbound.IAgentTeamPort`.

**Kurduğu bileşenler:**
```
CustomerSupportTeam
    ├── AgentTeamFactory (new)   ← chatClient, prompts, approvalGate, tools, guards, loggerFactory
    ├── TurnFinalizer (new)      ← traceStore, approvalGate, loggerFactory, semanticMemory?, profileService?
    ├── WorkflowRunner (new)     ← factory, finalizer, contextPipeline, chatClient, guards, traceStore,
    │                              prompts, approvalGate, uiHint, approvalContext, loggerFactory
    └── DecomposedRunner (new)   ← _runner (WorkflowRunner), parallelOptions, uiHint, approvalContext
```

**Kimler çağırır:** `ChatPortService`, `EvaluationRunner` (`Adapters.Agents/Evaluation/`), `ReplanService` — `IAgentTeamPort` arayüzü üzerinden, somut tipten habersiz.

## Kullanılma nedeni ve tasarım yaklaşımı

Hexagonal mimaride bu sınıf, Application katmanının `IAgentTeamPort` portunu MAF (Microsoft Agent Framework) kullanarak implemente eden **Driven Adapter**'dır. Gerçek iş mantığının alt bileşenlere bölünmüş olması, `CustomerSupportTeam`'i test edilebilir ve okunabilir tutar — kompozisyon kökü olarak yalnızca "hangi nesne hangi bağımlılıklarla kurulur ve istek nereye gider" sorularına cevap verir, "nasıl çalışır" sorusuna değil.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunAsync(query, conversationHistory, session, reasoning, ct)` | Compound query ise `DecomposedRunner.RunDecomposedAsync`'e, değilse `WorkflowRunner.RunAsync`'e devreder. |
| `RunStreamingAsync(query, conversationHistory, session, reasoning, ct)` | Compound query ise `DecomposedRunner.RunDecomposedStreamingAsync`'e, değilse `WorkflowRunner.RunStreamingAsync`'e devreder. |
| `GetWorkflowDiagram()` | `WorkflowRunner.GetWorkflowDiagram()`'a devreder. |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IContextPipeline contextPipeline`, `IOptions<WorkflowGuardOptions> guardOptions`, `IOptions<ParallelExecutionOptions> parallelOptions`, `IReasoningTraceStore traceStore`, `IPromptRepository prompts`, `ApprovalGateService approvalGate`, `ICustomerSupportToolsService tools`, `IUiHintEmitter uiHint`, `IApprovalContextAccessor approvalContext`, `ILoggerFactory loggerFactory`, `ISemanticMemoryWriter? semanticMemory = null`, `ICustomerProfileService? profileService = null`.
