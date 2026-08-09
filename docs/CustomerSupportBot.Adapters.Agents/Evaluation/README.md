# Evaluation — Genel Bakış

`CustomerSupportBot.Adapters.Agents/Evaluation/` klasörü, `docs/evaluation-scenarios.yaml`'daki senaryoları gerçek sistem üzerinde otomatik çalıştıran ve sonuçları değerlendiren bileşenleri barındırır.

> **Konum notu:** Bu klasör önceden `CustomerSupportBot.Application/Services/Evaluation/`'daydı. `CriteriaEvaluator`, `Microsoft.Agents.AI`'ın gerçek `EvalCheck`/`EvalItem`/`EvalChecks`/`FunctionEvaluator` tiplerini kullanacak şekilde yeniden tasarlandığında, `CustomerSupportBot.Application` projesinin **kasıtlı olarak sadece Domain'e bağımlı** olması gerektiği (csproj'da açıkça belirtilir, MAF paket referansı yok) nedeniyle bu iki dosya `Adapters.Agents`'a taşındı. `IEvaluationPort` arayüzü (Application/Ports/Inbound) yerinde kaldı; yalnızca implementasyonu ve DI kaydı (`AgentsAdapterServiceCollectionExtensions.AddAgentsAdapter`) buraya taşındı.

## Dosyalar

| Dosya | Açıklama |
|---|---|
| [EvaluationRunner](EvaluationRunner.md) | `IEvaluationPort` implementasyonu — senaryoları gerçek workflow'a karşı çalıştırır. |
| [CriteriaEvaluator](CriteriaEvaluator.md) | Yapılandırılmış (typed) `success_criteria`'ları MAF `EvalCheck` tipleri üzerinden değerlendirir. |

## Akış

```
docs/evaluation-scenarios.yaml (ScenarioLoader ile yüklenir, CustomerSupportBot.Api/Infrastructure/)
    │
    ▼
EvaluationRunner.RunAsync(scenarios)
    ├─ Senaryo #1 → İzole session → ReasoningService.ReasonAsync → IAgentTeamPort.RunAsync → CriteriaEvaluator
    ├─ Senaryo #2 → ...
    └─ ...
    │
    ▼
EvaluationRunResult (JSON, /eval/run endpoint'i döner)
```

Detaylı şema/type tablosu ve API sözleşmesi için bkz. [docs/evaluation.md](../../evaluation.md).
