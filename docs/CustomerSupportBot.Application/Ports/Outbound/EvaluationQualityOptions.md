# EvaluationQualityOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/EvaluationQualityOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`EvaluationQualityOptions`, Application/Ports/Outbound/EvaluationQualityOptions.cs MEAI (Microsoft.Extensions.AI.Evaluation.Quality) LLM-judge kalite kontrollerinin (relevance/coherence) global açma/kapama anahtarı. <summary> Evaluation senaryolarındaki <c>quality_checks</c> (relevance/coherence — MEAI LLM-judge evaluator'ları) için global kapı. Varsayılan <c>false</c>: her koşum gerçek bir ek LLM çağrısı (judge modeli) gerektirdiği için maliyetli — senaryo bunları istese bile bu <c>false</c> olduğu sürece atlanır (<c>Skipped="quality_checks_disabled"</c>). CI'da ayrı, isteğe bağlı bir job'da <c>true</c> yapılması önerilir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`EvaluationQualityOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
