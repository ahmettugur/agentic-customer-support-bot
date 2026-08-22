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
  1. `_sessionManager.GetOrCreateAsync` ile taze oturum açılır.
  2. `_reasoningService.ReasonAsync` ile sorgu analiz edilir.
  3. `_team.RunAsync` ile çoklu ajan iş akışı koşturulur.
  4. Trace kaydı `_traceStore.GetBySession` üzerinden çekilir.
  5. [CriteriaEvaluator.EvaluateAll](CriteriaEvaluator.md) ile senaryo başarı kriterleri puanlanır ve `ScenarioResult` döndürülür.

### 4. `AggregateRepetitions` (Internal Static)
- **Ne işe yarar?:** N adet koşu sonucunu (`List<ScenarioResult>`) tek bir sonuç nesnesine indirger; `Repetitions`, `RepetitionOutcomes` ve `RepetitionPassRate` oranlarını hesaplar.

## Bağımlılıklar

- [IEvaluationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md)
- [IAgentTeamPort](../../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md)
- [IReasoningPort](../../CustomerSupportBot.Application/Ports/Inbound/IReasoningPort.md)
- [CriteriaEvaluator](CriteriaEvaluator.md)
- [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
