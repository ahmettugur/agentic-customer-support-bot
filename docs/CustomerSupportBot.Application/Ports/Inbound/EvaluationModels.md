# EvaluationModels.cs — Değerlendirme (Evaluation) DTO Ailesi

**Dosya:** `Ports/Inbound/EvaluationModels.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

Bu dosyada `IEvaluationPort`'un kullandığı 7 küçük DTO/model birlikte tanımlıdır. Skill kuralı gereği her tip ayrı ayrı, eksiksiz belgelenmiştir — dosya tek tutulmuştur çünkü hepsi tek bir kavramsal grubun ("bir senaryo dosyasını okuyup koşturup sonucunu raporlamak") parçalarıdır ve ayrı dosyalara bölünmesi okunabilirliği azaltır.

## 1. Ne işe yarar? (genel)

Otomatik değerlendirme (evaluation) sistemi, `docs/evaluation-scenarios.yaml` gibi bir YAML dosyasından okunan senaryoları botun gerçek workflow'unda koşturup, botun davranışının beklentilerle örtüşüp örtüşmediğini raporlar. Bu dosyadaki tipler o sürecin **girdi** (senaryo tanımı) ve **çıktı** (sonuç raporu) şemasını oluşturur.

## 2. Hangi amaçla kullanılır?

`IEvaluationPort.RunAsync`/`RunScenarioAsync` bu tipleri parametre/dönüş değeri olarak kullanır; `CriteriaEvaluator` (Adapters.Agents) `CriterionSpec`'i işleyip `CriterionResult` üretir; `EvaluationRunner` (Adapters.Agents) senaryoları koşturup `ScenarioResult`/`EvaluationRunResult` doldurur.

## 3. Tipler

### `ScenarioFile`

Bir YAML senaryo dosyasının kök yapısı.

| Üye | Tip | Açıklama |
|---|---|---|
| `Version` | `int` | Şema versiyonu (varsayılan `1`). |
| `Scenarios` | `List<EvaluationScenario>` | Dosyadaki senaryoların listesi. |

### `EvaluationScenario`

Tek bir test senaryosu — bir kullanıcı sorgusu ve onun için beklenen davranış.

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Senaryonun benzersiz kimliği. |
| `Category` | `string` | Raporlamada gruplama için kategori adı. |
| `Query` | `string` | Bota gönderilecek kullanıcı sorgusu. |
| `ExpectedIntent` | `string?` | Beklenen niyet sınıflandırması. |
| `ExpectedBehavior` | `string?` | Beklenen davranışın serbest metin açıklaması (dokümantasyon amaçlı, otomatik kontrol edilmez). |
| `ExpectedAgents` | `List<string>` | Sorgunun yönlendirilmesi beklenen uzman ajan(lar). |
| `ExpectedTools` | `List<string>` | Çağrılması beklenen tool adları — yalnızca isim listesi (`no_extra_tool_calls` kriteri için). |
| `ExpectedToolCalls` | `List<ExpectedToolCallSpec>` | İsim+argüman eşleşmesi bekleyen, `tool_call_args_match` kriteri için kullanılan daha katı liste. `ExpectedTools`'tan bağımsızdır. |
| `SuccessCriteria` | `List<CriterionSpec>` | Senaryonun geçip geçmediğini belirleyen yapılandırılmış kriterler. |
| `KnownFailureMode` | `string?` | Bilinen bir kusur varsa açıklaması (dokümantasyon amaçlı). |
| `Repetitions` | `int` | Senaryonun kaç kez koşturulacağı (varsayılan `1`). LLM çıktısındaki non-determinism'i ölçmek için `1`'den büyük verilebilir; `ScenarioResult.RepetitionOutcomes`/`RepetitionPassRate` bu durumda doldurulur. |
| `QualityChecks` | `List<string>` | MEAI kalite değerlendiricileri (`"relevance"`, `"coherence"` gibi). Boşsa hiç çalıştırılmaz; global `EvaluationQualityOptions.Enabled=false` (varsayılan) iken senaryo istese bile atlanır çünkü gerçek bir LLM-judge çağrısı gerektirir (maliyetli, opt-in). |

### `ExpectedToolCallSpec`

YAML'da `expected_tool_calls` altında tanımlanan tek bir tool çağrı beklentisi.

| Üye | Tip | Açıklama |
|---|---|---|
| `Name` | `string` | Beklenen tool adı. |
| `Arguments` | `Dictionary<string, object>?` | `null` ise sadece isim kontrol edilir; doluysa argümanlar bir alt-küme (subset) eşleşmesiyle kontrol edilir (fazladan argüman sorun değildir). |

### `CriterionSpec`

Yapılandırılmış (typed) bir başarı kriteri — `CriteriaEvaluator`'daki dispatch table'ın anahtarı `Type`'tır, diğer alanlar kriter türüne göre kullanılır/yoksayılır.

| Üye | Tip | Açıklama |
|---|---|---|
| `Type` | `string` | Dispatch anahtarı: `contains_any`, `tool_called`, `tool_not_called`, `turn_count`, `manual_review` vb. |
| `Values` | `List<string>?` | `contains_any`/`tool_called`/`tool_not_called` için değer listesi. |
| `Op` | `string?` | `turn_count`/`iteration_count` için karşılaştırma operatörü (`"<="`, `"=="` vb.). |
| `Value` | `int?` | `turn_count`/`iteration_count` için eşik değer. |
| `Field` | `string?` | `agent_requests_field` için beklenen alan adı (ör. `"customer_id"`). |
| `Note` | `string?` | `manual_review` için gözden geçirme notu. |

### `ScenarioResult`

Tek bir senaryonun çalıştırılma sonucu.

| Üye | Tip | Açıklama |
|---|---|---|
| `ScenarioId`, `Category`, `Query` | `string` | Kaynak senaryodan kopyalanır. |
| `TraceId` | `string?` | Bu koşuya ait `ReasoningTrace` kimliği (debug için). |
| `PassedCriteria` / `TotalCriteria` | `int` | Geçen/toplam kriter sayısı. |
| `Passed` | `bool` (hesaplanan) | `TotalCriteria > 0 && PassedCriteria == TotalCriteria`. |
| `CriteriaResults` | `List<CriterionResult>` | Her kriterin ayrıntılı sonucu. |
| `Response` | `string?` | Botun gerçek yanıtı. |
| `TerminationReason` | `string?` | Turun nasıl sonlandığı. |
| `DetectedIntent` | `string?` | Gerçekleşen niyet sınıflandırması. |
| `AgentsVisited` | `List<string>` | Gerçekte devreye giren ajanlar. |
| `ToolsCalled` | `List<string>` | Gerçekte çağrılan tool'lar. |
| `DurationMs` | `long` | Koşum süresi. |
| `Error` | `string?` | Koşum sırasında oluşan hata varsa. |
| `Repetitions` | `int` | Kaç kez koşturulduğu (varsayılan `1`). |
| `RepetitionOutcomes` | `List<bool>?` | `Repetitions > 1` ise her koşunun `Passed` sonucu; tek koşuda `null` (gereksiz JSON gürültüsü üretmemek için). |
| `RepetitionPassRate` | `double?` | `Repetitions > 1` ise geçme oranı (0.0–1.0) — non-determinism ölçüsü. |

### `CriterionResult`

Tek bir kriterin değerlendirme sonucu.

| Üye | Tip | Açıklama |
|---|---|---|
| `Criterion` | `string` | Kriterin tanımı/adı. |
| `Passed` | `bool` | Geçti mi. |
| `Evaluation` | `string` | Değerlendirmenin açıklaması. |
| `Skipped` | `string?` | Atlandıysa sebebi (ör. `"manual_review_needed"`). |

### `EvaluationRunResult`

Tüm bir evaluation koşusunun (birden çok senaryo) özeti.

| Üye | Tip | Açıklama |
|---|---|---|
| `StartedAt` / `CompletedAt` | `DateTime` | Koşunun zaman aralığı. |
| `TotalScenarios` / `PassedScenarios` / `FailedScenarios` / `PartialScenarios` | `int` | Senaryo sayaçları. |
| `PassRate` | `double` (hesaplanan) | `PassedScenarios / TotalScenarios` (senaryo yoksa `0.0`). |
| `Results` | `List<ScenarioResult>` | Her senaryonun ayrıntılı sonucu. |

## 4. Diğer katman/bileşenlerle ilişkileri

- `IEvaluationPort` bu tipleri kullanır.
- `EvaluationRunner`, `CriteriaEvaluator`, `ScenarioLoader` (Adapters.Agents / Api katmanı) bu tipleri üretir/tüketir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Tipli DTO'lar kullanmanın nedeni, YAML'dan okunan senaryo tanımlarının derleme zamanında doğrulanabilir olmasıdır — alan adı yanlış yazıldığında çalışma zamanı yerine deserialize aşamasında/derleme aşamasında fark edilir.

## 6. Bağımlılıklar

Yok — saf DTO grubu, hiçbir servise bağımlı değil.

## Bağlantılar

- [IEvaluationPort](IEvaluationPort.md)
