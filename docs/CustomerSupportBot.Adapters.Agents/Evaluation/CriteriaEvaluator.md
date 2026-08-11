# CriteriaEvaluator

> 💡 **Analiz notu:** Test hakemi — bot'un yanıtını kriterlerle karşılaştırır (doğruluk, eksiksizlik, ton). MAF'ın `FunctionEvaluator` altyapısını kullanır.

**Dosya:** `CustomerSupportBot.Adapters.Agents/Evaluation/CriteriaEvaluator.cs`
**Erişim:** `public static` (+ `public class ScenarioRunContext`, aynı dosyada)

## Ne işe yarar?

`docs/evaluation-scenarios.yaml`'daki yapılandırılmış (typed) `success_criteria` girdilerini, `Microsoft.Agents.AI`'ın gerçek `EvalCheck`/`EvalItem`/`EvalCheckResult` tipleri üzerinden değerlendirir.

## Hangi amaçla kullanılır?

`EvaluationRunner.RunScenarioAsync`, her senaryonun her kriterini `CriteriaEvaluator.Evaluate(spec, evalItem, ctx)` ile değerlendirip `CriterionResult` toplar.

## Sorumlulukları

- `CriterionSpec.Type` alanına göre bir dispatch table (`Dictionary<string, Func<CriterionSpec, ScenarioRunContext, EvalCheck>>`) üzerinden doğru `EvalCheck` delegate'ini kurmak.
- Built-in eşleşen tipler için framework'ün gerçek `EvalChecks`/`FunctionEvaluator` tiplerini kullanmak (ör. `tool_called` → `EvalChecks.ToolCalledCheck`, `tool_call_args_match` → `EvalChecks.ToolCallArgsMatch`).
- Bu uygulamaya özgü tipler için `FunctionEvaluator.Create` ile custom closure'lar yazmak (`turn_count`, `customer_id_used`, `agent_requests_field` vb.) — bunlar `ScenarioRunContext`'i closure-capture ile kullanır çünkü `EvalItem`'ın sabit şekli (`Query`/`Response`/`Conversation`/`Tools`/`ExpectedOutput`/`ExpectedToolCalls`) bu uygulamaya özgü alanları (`IterationCount`, `SpecialistReasonings`, `TerminationReason`) taşımaz.
- Bilinmeyen `type` veya açık `type: manual_review` → her ikisi de `Passed=false, Skipped="manual_review_needed"` (exception atmaz, fail-safe).

**Üstlenmediği işler:** `EvalItem`'ın (özellikle sentetik `Conversation`) inşası — `EvaluationRunner`'ın sorumluluğu.

## Diğer katman ve bileşenlerle ilişkileri

**Kullandığı MAF tipleri:** `Microsoft.Agents.AI.EvalCheck` (`delegate EvalCheckResult EvalCheck(EvalItem item)`), `EvalItem`, `EvalCheckResult`, `EvalChecks` (`ToolCalledCheck`, `ToolCallArgsMatch`), `FunctionEvaluator.Create`.

**Kimler çağırır:** `EvaluationRunner.RunScenarioAsync` (bkz. [EvaluationRunner.md](EvaluationRunner.md)).

**Girdi tipleri:** `CustomerSupportBot.Application.Ports.Inbound.CriterionSpec` (Application katmanı — framework-agnostic DTO), `ScenarioRunContext` (bu dosyada tanımlı, framework-agnostic).

## Kullanılma nedeni ve tasarım yaklaşımı

Önceki tasarım (`CriteriaEvaluator.Evaluate(string criterion, ScenarioRunContext ctx)`) `success_criteria`'yı serbest metin olarak regex/keyword sniffing ile yorumluyordu — kırılgan (yeni bir cümle kalıbı sessizce `manual_review_needed`'a düşer) ve MAF'ın gerçek eval tiplerinden habersizdi. Bu sınıf `CriterionSpec.Type` üzerinden tipli bir dispatch'e geçerek hem kırılganlığı giderdi hem framework'ün built-in check'lerini gerçekten kullanır hale geldi.

**Neden `Application` katmanında değil:** `CustomerSupportBot.Application` projesi kasıtlı olarak sadece `Domain`'e bağımlı (csproj'da açıkça belirtilir) — `Microsoft.Agents.AI` referansı yok. Bu sınıf gerçek MAF tiplerini kullandığı için (hexagonal mimari kuralına uymak amacıyla) `Adapters.Agents`'a taşındı; `EvaluationRunner` de aynı sebeple beraberinde taşındı (ikisi birbirini çağırır, ikisi de MAF tiplerine dokunur).

**Kapsam notu:** Bu redesign yeni otomasyon kapasitesi eklemedi — önceki regex/keyword-sniffing'in tanıdığı ~9 kalıp 1:1 typed karşılığına taşındı; eskiden de fiilen `manual_review_needed`'a düşen kriterler (`no_hallucinated_price`, `refuses to comply` vb.) şimdi açıkça `type: manual_review` olarak işaretleniyor. Davranış değişikliği yok, yalnızca dispatch mekanizması netleşti.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `Evaluate(spec, item, ctx)` (static) | Ana giriş noktası — `spec.Type`'a göre dispatch, sonucu `CriterionResult`'a adapte eder. |
| `Checks` (private static readonly `Dictionary`) | Dispatch table — desteklenen 12 `type` (bkz. [docs/evaluation.md §3](../../evaluation.md)). |
| `DescribeCriterion(spec)` (private static) | Rapor/API çıktısı için insan-okunur bir `Criterion` string'i üretir. |
| `CompareResult(checkName, actual, op, value)` (private static) | `turn_count`/`iteration_count` için operatör karşılaştırması (`<=`, `<`, `==`, `>=`, `>`, `=`). |
| `CalledToolNames(item)` (private static) | `item.Conversation`'dan `FunctionCallContent.Name`'leri toplar. |
| `SafeIsMatch(input, pattern)` (private static) | ReDoS koruması için 500ms timeout'lu `Regex.IsMatch`. |
| `ScenarioRunContext` (aynı dosyada, public class) | `Response`, `TerminationReason`, `DetectedIntent`, `IterationCount`, `ToolsCalled`, `AgentsVisited`, `ExpectedTools`, `SpecialistReasonings`, `Reasoning`, `Planning` — closure-capture edilen bu uygulamaya özgü bağlam. |

## Desteklenen `type`'lar

Tam tablo için bkz. [docs/evaluation.md §3](../../evaluation.md) — `contains_any`, `tool_called`, `tool_not_called`, `turn_count`, `iteration_count`, `no_extra_tool_calls`, `no_missing_param_tool`, `agent_requests_field`, `complaint_id_returned`, `order_id_returned`, `customer_id_used`, `order_status_contains`, `tool_call_args_match`, `manual_review`.

## Bağımlılıklar

Yok (statik sınıf, DI'a kayıtlı değil).
