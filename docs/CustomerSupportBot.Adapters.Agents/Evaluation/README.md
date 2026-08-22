# CustomerSupportBot.Adapters.Agents.Evaluation

Bu klasör, Microsoft Agents Framework (MAF) değerlendirme sınıflarını (`EvalCheck`, `EvalItem`, `FunctionEvaluator`) kullanarak `evaluation-scenarios.yaml` senaryolarını otomatize eden test ve kalite denetim motorunu barındırır.

## Dosyalar

- [EvaluationRunner](EvaluationRunner.md) — [IEvaluationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md) portunu uygulayan ve senaryoları izole oturumlarda çalıştıran ana değerlendirme koşucusu.
- [CriteriaEvaluator](CriteriaEvaluator.md) — Senaryo başarı kriterlerini (`contains_any`, `tool_called`, `turn_count`, `no_extra_tool_calls` vb.) MAF `EvalCheck` nesneleri üzerinden denetleyen değerlendirici.
