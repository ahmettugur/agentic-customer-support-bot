# AgentTeamFactory

**Dosya:** `CustomerSupportBot.Adapters.Agents/AgentTeamFactory.cs`
**Erişim:** `internal sealed`
**Yaşam döngüsü:** Singleton (`CustomerSupportTeam` içinde `new` ile kurulur)

## Ne işe yarar?

6 ajanı (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`) bir kez örnekler ve her workflow koşusu için **taze** bir `Microsoft.Agents.AI.Workflows.Workflow` üretir.

## Hangi amaçla kullanılır?

`WorkflowRunner`, her `RunAsync`/`RunStreamingAsync` çağrısında `CreateWorkflow()`'u çağırır. Ajan örnekleri (dolayısıyla tool bağlamları ve `IChatClient` bağlantıları) süreç ömrü boyunca sabittir — yalnızca `CustomerSupportChatManager` (yönlendirme durumu, `_handoffCounts` dahil) ve graph bağlantıları her koşuda yeniden kurulur, böylece tur-başına izole olması gereken state garanti edilir.

## Sorumlulukları

- 6 ajanı, kendi `Team/*.cs` sınıflarından örnekleyip her birini `UseOpenTelemetry` ile sarmalamak (`WrapWithTelemetry`).
- `AgentWorkflowBuilder.CreateGroupChatBuilderWith(...)` ile her koşuda yeni bir `CustomerSupportChatManager` kurup 6 ajanı `AddParticipants` ile graph'a eklemek.

**Üstlenmediği işler:** Ajanların kendi prompt/tool/schema tanımları (bkz. `Team/*.cs`), routing kararı (`CustomerSupportChatManager`), workflow'un çalıştırılması/event işlenmesi (`WorkflowRunner`).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`, `ApprovalGateService`, `ICustomerSupportToolsService`, `WorkflowGuardOptions`, `ILoggerFactory` — tamamı `Team/*.cs` ajan constructor'larına ve `CustomerSupportChatManager`'a geçirilir.

**Kimler çağırır:** `WorkflowRunner.RunAsync`/`RunStreamingAsync`/`GetWorkflowDiagram` — her biri `_factory.CreateWorkflow()` çağırır.

**Neyi örnekler:** `CustomerSupportBot.Adapters.Agents.Team` namespace'indeki `PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent` (bkz. [Team/README.md](Team/README.md)); `CustomerSupportChatManager` (bkz. [CustomerSupportChatManager.md](CustomerSupportChatManager.md)).

## Kullanılma nedeni ve tasarım yaklaşımı

Ajan örneklerinin (dolayısıyla tool/prompt kurulumunun) her turda yeniden yaratılması gereksiz maliyet olurdu — bu yüzden yalnızca bir kez örneklenip `readonly` property olarak tutulur. Buna karşın `CustomerSupportChatManager` (özellikle `_handoffCounts` gibi tur-içi state taşıyan alanları) her `CreateWorkflow()` çağrısında **yeniden** kurulur — aksi halde bir önceki turun handoff sayaçları bir sonraki bağımsız turu etkilerdi. Bu ayrım (ajan = süreç-ömürlü, chat manager = tur-ömürlü) sınıfın tek amacıdır.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `PlanningAgent` / `ProductAgent` / `OrderAgent` / `ComplaintAgent` / `HumanHandoffAgent` / `ResponseAgent` (`AIAgent`, get-only) | Süreç ömrü boyunca sabit ajan örnekleri, OpenTelemetry ile sarmalanmış. |
| `CreateWorkflow()` | Yeni bir `CustomerSupportChatManager` + 6 katılımcıyla taze bir `Workflow` üretir. |
| `WrapWithTelemetry(agent, sourceName)` (private static) | `agent.AsBuilder().UseOpenTelemetry(sourceName).Build()`. |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`, `ApprovalGateService approvalGate`, `ICustomerSupportToolsService tools`, `WorkflowGuardOptions guards`, `ILoggerFactory loggerFactory`.
