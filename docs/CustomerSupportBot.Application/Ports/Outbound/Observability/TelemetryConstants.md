# TelemetryConstants

**Kaynak:** `Ports/Outbound/Observability/TelemetryConstants.cs`

## 1. Ne İşe Yarar

OpenTelemetry `ActivitySource` ve `Meter` isimlerini tek bir noktada sabitleyen statik sınıf.

## 2. Hangi Amaçla Kullanılır

`Adapters.Telemetry` katmanındaki OTel kaynak kaydı (tracing/metrics kurulumu) ve
`CustomerSupportTelemetry` gibi sınıflar `Activity`/`Meter` oluştururken bu sabitleri kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca isim sabitlerini tutmak.
- **Üstlenmediği:** OTel exporter/pipeline kurulumu — bu Adapters.Telemetry'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Telemetry/OpenTelemetry/CustomerSupportTelemetry` ve OTel kurulum extension'ları bu
sabitleri `new ActivitySource(TelemetryConstants.ActivitySourceName)` gibi kullanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Tek doğruluk kaynağı ilkesi: `ActivitySource`/`Meter` adı birden fazla yerde (OTel SDK kurulumu
+ kaynak oluşturma noktaları) aynı string olarak geçmek zorundadır — string'ler farklı
yazılırsa metrikler/trace'ler sessizce toplanmaz. Sabitler bunu derleme zamanında garanti eder.

## 6. Metotlar / Üyeler

| Üye | Değer | Açıklama |
|---|---|---|
| `const string ActivitySourceName` | `"CustomerSupportBot.Api"` | Tracing `ActivitySource` adı. |
| `const string MeterName` | `"CustomerSupportBot.Api"` | Metrics `Meter` adı. |

## 7. Bağımlılıklar

Yok.
