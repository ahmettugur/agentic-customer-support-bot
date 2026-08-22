# EvaluationRunner

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Evaluation/EvaluationRunner.cs`
- **Tür:** `public class : IEvaluationPort`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Evaluation`

## Ne işe yarar?

`EvaluationRunner`, Application katmanındaki [IEvaluationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md) portunu uygulayan; YAML formatındaki test senaryolarını (`EvaluationScenario`) izole oturumlarda otomatik olarak çalıştıran, reasoning + workflow + LLM değerlendirme adımlarını yürüten ve ayrıntılı başarı raporu (`EvaluationRunResult`) üreten değerlendirme motorudur.

## Hangi amaçla kullanılır`?

- **Regresyon ve Kalite Testleri:** Model, prompt veya araç değişikliklerinin mevcut senaryoları bozup bozmadığını test etmek.
- **Tekrarlı Koşu ve Determinizm Ölçümü (`Repetitions`):** `scenario.Repetitions > 1` olduğunda senaryoyu N kez koşturarak modelin kararlılığını (`RepetitionPassRate`) ölçmek.
- **İzole Oturumlar:** Her senaryo için taze bir `AgentSession` oluşturarak önceki testlerin bağlam kirliliği yaratmasını engellemek.
- **Kriter Değerlendirmesi:** Senaryo çıktılarını [CriteriaEvaluator](CriteriaEvaluator.md) üzerinden MAF `EvalCheck` kurallarıyla doğrulamak.

## Sorumlulukları

- **Üstlendiği:**
  - `IEvaluationPort.RunAsync` sözleşmesini karşılamak.
  - Senaryoları sırayla koşturup genel istatistikleri (`PassedScenarios`, `FailedScenarios`, `PartialScenarios`) toplamak.
  - `RunScenarioAsync` ile tekil ve tekrarlı senaryoları çalıştırmak.
  - `AggregateRepetitions` ile N koşunun sonuçlarını özetlemek.

## Constructor ve Başlatma Mantığı

```csharp
public EvaluationRunner(
    IAgentTeamPort team,
    IReasoningPort reasoningService,
    ISessionManager sessionManager,
    IReasoningTraceStore traceStore,
    IChatClient chatClient,
    IOptions<EvaluationQualityOptions> qualityOptions)
```

### Constructor İçerisinde Yapılan İşler:
- Ajan takımı (`_team`), muhakeme servisi (`_reasoningService`), oturum yöneticisi (`_sessionManager`), trace ambarı (`_traceStore`), LLM istemcisi (`_chatClient`) ve değerlendirme kalite seçenekleri (`_qualityOptions`) bağımlılıkları saklanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `RunAsync`
```csharp
public async Task<EvaluationRunResult> RunAsync(
    List<EvaluationScenario> scenarios,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Verilen senaryo listesini koşturup `EvaluationRunResult` raporu döner.
- **İç Mantığı:** Senaryoları `foreach` döngüsünde `RunScenarioAsync` ile çalıştırır; tam geçenleri (`Passed`), kısmi geçenleri (`PartialScenarios`) ve kalanları (`FailedScenarios`) sayar.

### 2. `RunScenarioAsync`
```csharp
public async Task<ScenarioResult> RunScenarioAsync(
    EvaluationScenario scenario,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Tek bir senaryoyu (varsa N tekrarlı olarak) koşturur.
- **İç Mantığı:** `scenario.Repetitions <= 1` ise doğrudan `RunSingleAsync` çağrılır. `> 1` ise döngüde N kez çalıştırılıp `AggregateRepetitions` ile birleştirilir.

### 3. `RunSingleAsync` (Private)
- **Ne işe yarar?:** Senaryonun tek bir koşusunu yürütür.
- **İç Mantığı:**
  1. `_sessionManager.GetOrCreateAsync(null, ct)` ile taze/izole bir oturum açılır (senaryolar birbirinin bağlamını kirletmesin diye).
  2. `_reasoningService.ReasonAsync` ÖNCE çalıştırılır ki `PlanningAgent` ön-analiz bağlamını alsın; ardından `_team.RunAsync` ile çoklu ajan iş akışı koşturulur.
  3. Session'a ait EN SON trace `_traceStore.GetBySession(session.SessionId).LastOrDefault()` ile çekilir; `AgentsVisited`, `ToolsCalled` (trace'in gerçek `ToolCalls` listesinden, ajan adından TAHMİN edilmez) buradan türetilir.
  4. `ScenarioRunContext` doldurulur ve MAF built-in check'lerin (`EvalChecks.ToolCalledCheck` vb.) tarayabileceği SENTETİK bir `EvalItem`/`conversation` kurulur — gerçek `ChatMessage` geçmişi trace'te tutulmadığı için, bilinen tool adlarından `FunctionCallContent` içeren yapay asistan mesajları üretilir.
  5. `scenario.SuccessCriteria` listesindeki HER kriter için `foreach` döngüsünde ayrı ayrı [CriteriaEvaluator.Evaluate](CriteriaEvaluator.md) çağrılır (toplu bir "EvaluateAll" yoktur) ve sonuçlar `result.CriteriaResults`'a eklenir.
  6. Varsa `scenario.ExpectedIntent` ayrıca kontrol edilir (`reasoning.Intent` içeriyor mu) ve bir `CriterionResult` olarak eklenir.
  7. Varsa `scenario.QualityChecks` (`relevance`, `coherence` gibi) her biri için `RunQualityCheckAsync` çağrılır.
  8. Süreç boyunca oluşan istisna `result.Error`'a yazılır (fırlatılmaz) — bir senaryonun patlaması diğer senaryoların koşmasını engellemesin diye; `result.DurationMs` her koşulda hesaplanır.

### 4. `RunQualityCheckAsync` (Internal)
```csharp
internal async Task<CriterionResult> RunQualityCheckAsync(
    string checkName, string query, string? response, CancellationToken ct)
```
- **Ne işe yarar?:** Microsoft.Extensions.AI.Evaluation'ın LLM-judge kalite değerlendiricilerini (`RelevanceEvaluator`, `CoherenceEvaluator`) çalıştırıp yanıtın kalitesini puanlar.
- **İç Mantığı:** `EvaluationQualityOptions.Enabled` (appsettings `EvaluationQuality:Enabled`, varsayılan `false`) kapalıysa GERÇEK bir LLM çağrısı yapılmadan `Skipped="quality_checks_disabled"` döner — her koşum ek bir judge-model çağrısı gerektirdiği için maliyetli, opt-in bir özelliktir. `checkName` `"relevance"`/`"coherence"` dışında bir değerse `ArgumentOutOfRangeException` fırlatılır. Sonuç metriği `NumericMetric.Interpretation.Failed`'e göre `Passed`'e çevrilir; metrik hiç dönmezse `Skipped="manual_review_needed"`.

### 5. `AggregateRepetitions` (Internal Static)
- **Ne işe yarar?:** N adet koşu sonucunu (`List<ScenarioResult>`) tek bir sonuç nesnesine indirger; `Repetitions`, `RepetitionOutcomes` ve `RepetitionPassRate` oranlarını hesaplar. Saf/deterministiktir — LLM veya I/O gerektirmez, izole test edilebilir.

## Bağımlılıklar

- [IEvaluationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md)
- [IAgentTeamPort](../../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md)
- [IReasoningPort](../../CustomerSupportBot.Application/Ports/Inbound/IReasoningPort.md)
- [CriteriaEvaluator](CriteriaEvaluator.md)
- [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
