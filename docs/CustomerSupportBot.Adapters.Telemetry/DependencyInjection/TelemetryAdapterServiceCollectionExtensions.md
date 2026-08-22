# TelemetryAdapterServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/DependencyInjection/TelemetryAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.DependencyInjection`

## Ne işe yarar?

`TelemetryAdapterServiceCollectionExtensions`, OpenTelemetry SDK altyapısını (`AddOpenTelemetry`), ASP.NET Core, HttpClient ve Entity Framework Core enstrümantasyonlarını, `CostCalculator` (`ICostCalculatorPort`) ve `CostUsageStore` (`ICostUsageStorePort`) servislerini DI IoC konteynerine kaydeden uzantıdır.

## Hangi amaçla kullanılır`?

- Composition Root (`CustomerSupportBot.Api`) tarafında tek satırla (`services.AddTelemetryAdapters(configuration)`) tüm dağıtık izleme ve metrik boru hattını ayağa kaldırmak.
- OTLP Exporter yapılandırmasını (`Grpc` veya `HttpProtobuf`) dinamik olarak oluşturmak.

## Sorumlulukları

- **Üstlendiği:**
  - `TelemetryOptions` ayarlarını bağlamak.
  - `ICostCalculatorPort` ➔ `CostCalculator` (Singleton) ve `CostUsageStore` (Singleton) kaydını yapmak.
  - OpenTelemetry Tracing (`AddSource`, `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddEntityFrameworkCoreInstrumentation`, `AddOtlpExporter`) boru hattını kurmak.
  - OpenTelemetry Metrics (`AddMeter`, `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddOtlpExporter`) boru hattını kurmak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `AddTelemetryAdapters`
```csharp
public static IServiceCollection AddTelemetryAdapters(
    this IServiceCollection services,
    IConfiguration configuration,
    string activitySourceName = "CustomerSupportBot",
    string meterName = "CustomerSupportBot")
```
- **Ne işe yarar?:** Telemetri servislerini ve OpenTelemetry boru hattını başlatır.
- **İç Mantığı:**
  1. `TelemetryOptions` bağlanır.
  2. `CostCalculator` ve `CostUsageStore` singleton olarak kaydedilir.
  3. `options.Enabled` aktif ise `services.AddOpenTelemetry()` zinciri işletilerek tracing ve metrics sağlayıcıları OTLP exporter ile bağlanır.

## Bağımlılıklar

- [ICostCalculatorPort](../OpenTelemetry/CostCalculator.md)
- [ICostUsageStorePort](../OpenTelemetry/CostUsageStore.md)
- [TelemetryOptions](../Options/TelemetryOptions.md)
- `OpenTelemetry.Trace.TracerProviderBuilder`
- `OpenTelemetry.Metrics.MeterProviderBuilder`
