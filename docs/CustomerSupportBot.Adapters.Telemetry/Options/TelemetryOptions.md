# TelemetryOptions

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/Options/TelemetryOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry` (dosya `Options/` klasöründe olsa da namespace klasör adını içermiyor — dikkat)

## Ne işe yarar?

`TelemetryOptions`, `appsettings.json` içerisindeki `"Telemetry"` bölümünü (`TelemetryOptions.SectionName = "Telemetry"`) strongly-typed C# modeline bağlayan seçenek sınıfıdır.

## Hangi amaçla kullanılır`?

- OpenTelemetry servis adını (`ServiceName`), sürümünü (`ServiceVersion`), Tracing ve Metrics bayraklarını yapılandırmak.
- OTLP Exporter uç noktasını (`Endpoint`), protokolünü (`Grpc`, `HttpProtobuf`) ve header bilgilerini ayarlamak.
- Model bazlı girdi/çıktı birim fiyatlandırma tablosunu (`Pricing`) tanımlamak.

## Alanlar ve Özellikler

### 1. Kök Yapılandırma (`TelemetryOptions`)
- `Enabled` (`bool`): Telemetrinin aktif olup olmadığı (varsayılan: `true`).
- `ServiceName` (`string`): Servis adı (`"CustomerSupportBot"`).
- `ServiceVersion` (`string`): Servis versiyonu (`"1.0.0"`).
- `TracingEnabled` (`bool`): Dağıtık izlemenin açık olup olmadığı.
- `MetricsEnabled` (`bool`): Metrik toplamanın açık olup olmadığı.
- `Otlp` (`OtlpExporterOptions`): OTLP aktarıcı ayarları.
- `Pricing` (`Dictionary<string, ModelPricing>`): Model başına 1K token fiyatlandırma tablosu.

### 2. `ModelPricing`
- `InputPer1K` (`decimal`): 1.000 input token başına USD maliyeti.
- `OutputPer1K` (`decimal`): 1.000 output token başına USD maliyeti.
