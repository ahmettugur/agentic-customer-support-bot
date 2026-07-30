# DependencyInjection (AgentsAdapterServiceCollectionExtensions)

**Dosya:** `CustomerSupportBot.Adapters.Agents/DependencyInjection/AgentsAdapterServiceCollectionExtensions.cs`

## Ne yapar?

`AddAgentsAdapter()` extension metodu, Agents adapter'ına ait servisleri DI container'a kaydeder. `Program.cs` veya `*ServiceCollectionExtensions.cs` içinde bir kez çağrılır.

## Kullanım

```csharp
// Program.cs veya ApplicationServicesExtensions.cs
services.AddAiServices(configuration);    // ← önce bu
services.AddAgentsAdapter();              // ← sonra bu
```

> **Ön koşul:** `IChatClient` bu metod çağrılmadan önce mutlaka kayıtlı olmalıdır. Kayıtlı değilse `InvalidOperationException` fırlatılır (fail-fast).

## Kayıtlı servisler

```csharp
services.AddSingleton<ApprovalGateService>();

services.AddSingleton<CustomerSupportTeam>();

services.AddSingleton<IAgentTeamPort>(sp =>
    sp.GetRequiredService<CustomerSupportTeam>());
```

| Servis | Kayıt tipi | Açıklama |
|--------|-----------|---------|
| `ApprovalGateService` | Singleton | HITL onay kapısı |
| `CustomerSupportTeam` | Singleton | Ana orkestratör — concrete tip olarak da erişilebilir |
| `IAgentTeamPort` | Singleton (factory) | Application katmanının kullandığı port arayüzü |

`CustomerSupportTeam` hem concrete tip (`CustomerSupportTeam`) hem de arayüz (`IAgentTeamPort`) olarak kayıtlıdır. Concrete tip `ApprovalGateService` gibi bileşenler tarafından doğrudan erişilebilir; Application katmanı yalnızca `IAgentTeamPort` üzerinden erişir.

## `IChatClient` ön koşul kontrolü

```csharp
if (!services.Any(d => d.ServiceType == typeof(IChatClient)))
{
    throw new InvalidOperationException(
        "IChatClient must be registered before calling AddAgentsAdapter(). ...");
}
```

Bu kontrol "fail-fast" prensibiyle çalışır: uygulama başlarken yanlış yapılandırma varsa açık bir hata mesajıyla hemen düşer, runtime'da sessizce başarısız olmaz.

## Opsiyonel servisler

`CustomerSupportTeam` constructor'ı `ISemanticMemoryWriter?` ve `ICustomerProfileService?` parametrelerini opsiyonel olarak alır. Bu servisler DI'a kayıtlıysa otomatik inject edilir; kayıtlı değilse null gelir ve özellik atlanır.

Semantic memory'yi etkinleştirmek için:
```csharp
services.AddSingleton<ISemanticMemoryWriter, YourSemanticMemoryImplementation>();
```

## Tüm bağımlılık zinciri

`AddAgentsAdapter()` çağrısının çalışması için aşağıdaki servislerin daha önce kayıtlı olması gerekir:

```
Zorunlu:
  IChatClient                    → AddAiServices() tarafından kayıtlı
  IContextPipeline               → Application services
  IOptions<WorkflowGuardOptions> → appsettings.json "WorkflowGuards:" bölümü (ValidateOnStart ile doğrulanır)
  IOptions<ParallelExecutionOptions> → appsettings.json "ParallelExecution:" bölümü (ValidateOnStart ile doğrulanır)
  IReasoningTraceStore           → Persistence adapter
  IPromptRepository              → FileSystemPromptRepository (Adapters.Persistence, Api projesi tarafından kayıtlı)
  ICustomerSupportToolsService   → Application services
  ApprovalGateService            → Bu extension tarafından kayıtlı
  IUiHintEmitter                 → Application services (UI ipucu yayıcı)
  IApprovalQueue                 → Application/Persistence
  IOptions<ApprovalOptions>      → appsettings.json "HumanInTheLoop:" bölümü (ValidateOnStart ile doğrulanır)
  IEscalationSink                → Persistence adapter
  IApprovalContextAccessor       → Application services
  EscalationPolicyService        → Application services
  ILoggerFactory                 → ASP.NET Core (otomatik)

Opsiyonel:
  ISemanticMemoryWriter          → Semantic memory adapter (varsa)
  ICustomerProfileService        → Profile service (varsa)
```

## `appsettings.json` gereksinimleri

```json
{
  "WorkflowGuards": {
    "TimeoutSeconds": 180,
    "MaxIterations": 20,
    "MaxDuplicateToolCalls": 3
  },
  "ParallelExecution": {
    "Enabled": true,
    "MaxDegreeOfParallelism": 4
  },
  "HumanInTheLoop": {
    "Enabled": true,
    "TimeoutSeconds": 120,
    "ToolsRequiringApproval": [
      "order_placement_tool",
      "order_cancel_tool",
      "return_request_tool",
      "complaint_registration_tool"
    ]
  }
}
```

> `MaxHandoffsPerAgent` (default 2) ve `PlanConfidenceThreshold` (default 0.7) `appsettings.json`'da tanımlı **değildir** — `WorkflowGuardOptions` sınıfındaki default değerler kullanılır. Yine de `ValidateOnStart()` bunları da doğrular.
>
> Hangi alt görevin paralel çalışabileceği config'den değil, `WellKnown.AgentNames.ReadOnly` kümesinden (şu an yalnızca `ProductAgent`) belirlenir.
