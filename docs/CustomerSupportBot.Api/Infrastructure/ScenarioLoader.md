# ScenarioLoader

- **Dosya:** `Infrastructure/ScenarioLoader.cs`
- **Namespace:** `CustomerSupportBot.Api.Infrastructure`

## 1. Ne İşe Yarar

`docs/evaluation-scenarios.yaml` dosyasını okuyup `ScenarioFile` (Application katmanı domain
tipi) nesnesine dönüştüren statik yardımcı sınıf.

## 2. Hangi Amaçla Kullanılır

[EvaluationEndpoints](../Endpoints/ObservabilityAndTelemetry.md) senaryo listeleme/koşturma
uçlarında YAML dosyasını her istekte yeniden okuyup ayrıştırmak için çağrılır (önbelleklenmez —
senaryo dosyası nadiren değişir ve dosya küçüktür, bu yüzden performans kaygısı yok).

## 3. Sorumlulukları

- YAML metnini `YamlDotNet` ile deserialize eder.
- `UnderscoredNamingConvention` kullanır: YAML alan adları `snake_case`, C# tarafı `PascalCase`.
- `IgnoreUnmatchedProperties()`: YAML'da tanımlı olup C# modelinde karşılığı olmayan alanlar
  hata fırlatmaz — senaryo dosyasına dokümantasyon amaçlı ekstra alan eklenebilmesini sağlar.
- **Üstlenmediği:** dosya yolunu bulma (`EvaluationEndpoints.ResolveScenarioPath`'in işi),
  senaryoları çalıştırma (`IEvaluationPort`'un işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ScenarioFile`, `EvaluationScenario` (Application katmanı domain/port modelleri) — deserialize
  hedefi.
- [ObservabilityAndTelemetry.md](../Endpoints/ObservabilityAndTelemetry.md) — tek çağıran.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`YamlDotNet` bağımlılığı **bilinçli olarak API katmanında izole edilmiştir** — Application
katmanı hangi serileştirme kütüphanesinin kullanıldığını bilmemeli; yalnızca `ScenarioFile`
tipini görür. Bu, hexagonal mimarideki "dış kütüphaneler adaptörlerde kalır" ilkesinin küçük
ölçekli bir örneğidir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static ScenarioFile LoadScenarios(string yamlPath)` | Verilen yoldaki YAML dosyasını okur ve `ScenarioFile`'a deserialize eder. Dosya yoksa/bozuksa `System.IO`/`YamlDotNet` istisnası fırlatır — çağıran (`EvaluationEndpoints`) bunu `try/catch` ile `Results.Problem`'e çevirir. |

## 7. Bağımlılıklar

Yok — tamamen statik, hiçbir servise bağımlı değil.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
