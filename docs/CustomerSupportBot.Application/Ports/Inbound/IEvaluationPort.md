# IEvaluationPort

**Dosya:** `Ports/Inbound/IEvaluationPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Değerlendirme (evaluation) senaryolarını koşturmak için primary port — bir veya birden çok senaryoyu gerçek workflow üzerinde çalıştırıp sonuç raporu üretir.

## 2. Hangi amaçla kullanılır?

Api katmanındaki (muhtemelen dev/admin-only) bir evaluation endpoint'i veya CLI/test aracı, YAML'dan okunan senaryoları bu port üzerinden koşturur; sonuç bir regresyon/kalite raporu olarak kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Bir senaryo listesini veya tek bir senaryoyu botun gerçek workflow'unda koşturup yapılandırılmış sonuç döndürmek.
- **Üstlenmediği:** Senaryoların nereden okunduğu (dosya sistemi, YAML parse) — bu `ScenarioLoader`'ın (Api katmanı) işidir; kriterlerin nasıl değerlendirildiği — bu `CriteriaEvaluator`'ın işidir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `EvaluationRunner` (Adapters.Agents).
- `ScenarioLoader` (Api) senaryoları YAML'dan okuyup bu porta besler.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Botun davranışını manuel test yerine otomatik, tekrarlanabilir senaryolarla doğrulamak için vardır — bir prompt/tool değişikliğinin regresyon yaratıp yaratmadığını hızlıca görmeyi sağlar.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<EvaluationRunResult> RunAsync(List<EvaluationScenario> scenarios, CancellationToken ct = default)` | Birden çok senaryoyu koşturup toplu rapor döner. |
| `Task<ScenarioResult> RunScenarioAsync(EvaluationScenario scenario, CancellationToken ct = default)` | Tek bir senaryoyu koşturur. |

## 7. Bağımlılıklar

[EvaluationModels.cs](EvaluationModels.md) içindeki tipler (`EvaluationScenario`, `ScenarioResult`, `EvaluationRunResult`).

## Bağlantılar

- [EvaluationModels](EvaluationModels.md)
