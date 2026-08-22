# ScenarioFile

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/EvaluationModels.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`ScenarioFile`, Ports/Driving/EvaluationModels.cs IEvaluationPort sözleşme tipleri — senaryo DTO'ları ve sonuç modelleri. <summary>Senaryo dosyasının kök yapısı: version + scenarios listesi.</summary> <summary>Tek bir evaluation senaryosu.</summary> <summary> Argüman-seviyeli tool çağrı beklentisi — <c>type: tool_call_args_match</c> kriteri için. <see cref="ExpectedTools"/>'tan bağımsız: o sadece isim listesi (no_extra_tool_calls için), bu ise isim+argüman eşleşmesi (subset match — fazladan argüman sorun değil) ister. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ScenarioFile`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Version` (`int`): İlgili veriyi temsil eden özellik.
- `Scenarios` (`List<EvaluationScenario>`): İlgili veriyi temsil eden özellik.
- `Id` (`string`): İlgili veriyi temsil eden özellik.
- `Category` (`string`): İlgili veriyi temsil eden özellik.
- `Query` (`string`): İlgili veriyi temsil eden özellik.
- `ExpectedIntent` (`string?`): İlgili veriyi temsil eden özellik.
- `ExpectedBehavior` (`string?`): İlgili veriyi temsil eden özellik.
- `ExpectedAgents` (`List<string>`): İlgili veriyi temsil eden özellik.
- `ExpectedTools` (`List<string>`): İlgili veriyi temsil eden özellik.
- `ExpectedToolCalls` (`List<ExpectedToolCallSpec>`): İlgili veriyi temsil eden özellik.
- `SuccessCriteria` (`List<CriterionSpec>`): İlgili veriyi temsil eden özellik.
- `KnownFailureMode` (`string?`): İlgili veriyi temsil eden özellik.
- `Repetitions` (`int`): İlgili veriyi temsil eden özellik.
- `QualityChecks` (`List<string>`): İlgili veriyi temsil eden özellik.
- `Name` (`string`): İlgili veriyi temsil eden özellik.
- `Type` (`string`): İlgili veriyi temsil eden özellik.
- `Values` (`List<string>?`): İlgili veriyi temsil eden özellik.
- `Op` (`string?`): İlgili veriyi temsil eden özellik.
- `Value` (`int?`): İlgili veriyi temsil eden özellik.
- `Field` (`string?`): İlgili veriyi temsil eden özellik.
- `Note` (`string?`): İlgili veriyi temsil eden özellik.
- `ScenarioId` (`string`): İlgili veriyi temsil eden özellik.
- `Category` (`string`): İlgili veriyi temsil eden özellik.
- `Query` (`string`): İlgili veriyi temsil eden özellik.
- `TraceId` (`string?`): İlgili veriyi temsil eden özellik.
- `PassedCriteria` (`int`): İlgili veriyi temsil eden özellik.
- `TotalCriteria` (`int`): İlgili veriyi temsil eden özellik.
- `CriteriaResults` (`List<CriterionResult>`): İlgili veriyi temsil eden özellik.
- `Response` (`string?`): İlgili veriyi temsil eden özellik.
- `TerminationReason` (`string?`): İlgili veriyi temsil eden özellik.
- `DetectedIntent` (`string?`): İlgili veriyi temsil eden özellik.
- `AgentsVisited` (`List<string>`): İlgili veriyi temsil eden özellik.
- `ToolsCalled` (`List<string>`): İlgili veriyi temsil eden özellik.
- `DurationMs` (`long`): İlgili veriyi temsil eden özellik.
- `Error` (`string?`): İlgili veriyi temsil eden özellik.
- `Repetitions` (`int`): İlgili veriyi temsil eden özellik.
- `RepetitionOutcomes` (`List<bool>?`): İlgili veriyi temsil eden özellik.
- `RepetitionPassRate` (`double?`): İlgili veriyi temsil eden özellik.
- `Criterion` (`string`): İlgili veriyi temsil eden özellik.
- `Passed` (`bool`): İlgili veriyi temsil eden özellik.
- `Evaluation` (`string`): İlgili veriyi temsil eden özellik.
- `Skipped` (`string?`): İlgili veriyi temsil eden özellik.
- `StartedAt` (`DateTime`): İlgili veriyi temsil eden özellik.
- `CompletedAt` (`DateTime`): İlgili veriyi temsil eden özellik.
- `TotalScenarios` (`int`): İlgili veriyi temsil eden özellik.
- `PassedScenarios` (`int`): İlgili veriyi temsil eden özellik.
- `FailedScenarios` (`int`): İlgili veriyi temsil eden özellik.
- `PartialScenarios` (`int`): İlgili veriyi temsil eden özellik.
- `Results` (`List<ScenarioResult>`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
