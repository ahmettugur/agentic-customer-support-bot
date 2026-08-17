# DependencyInjection — `AddTelemetryAdapters`

**Dosya:** `DependencyInjection/TelemetryAdapterServiceCollectionExtensions.cs`

OpenTelemetry pipeline'ını ve Cost altyapısını DI container'a kaydeder.

---

## İmza

```csharp
public static IServiceCollection AddTelemetryAdapters(
    this IServiceCollection services,
    IConfiguration configuration,
    string activitySourceName = "CustomerSupportBot",
    string meterName = "CustomerSupportBot")
```

`activitySourceName` ve `meterName` default değerleri `CustomerSupportTelemetry` static class ile uyumludur — değiştirmek isterse caller verebilir (test senaryoları için).

---

## Kayıt sırası

```
1. TelemetryOptions bind (configuration["Telemetry"])
2. ICostCalculatorPort  → CostCalculator (Singleton)
3. CostUsageStore       (Singleton)
4. ICostUsageStorePort  → CostUsageStore (factory ile)

5. options.Enabled == false ise → erken çıkış (OpenTelemetry yok)

6. OpenTelemetry resource: ServiceName + ServiceVersion
7. Tracing pipeline (options.TracingEnabled):
   - ActivitySource "CustomerSupportBot" dinle
   - ASP.NET Core, HttpClient, EF Core instrumentation
   - OTLP exporter (endpoint varsa)

8. Metrics pipeline (options.MetricsEnabled):
   - Meter "CustomerSupportBot" dinle
   - ASP.NET Core, HttpClient instrumentation
   - OTLP exporter (endpoint varsa)
```

---

## `options.Enabled = false` ne yapar?

```csharp
if (!options.Enabled)
    return services;
```

Cost altyapısı **kayıtlı kalır** (Application port'ları çalışır), ama OpenTelemetry hiç eklenmez:
- Span/metric üretilir ama dinleyen yok → atılır
- OTLP'ye bağlanılmaz
- ASP.NET Core/HttpClient/EF Core instrumentation devre dışı

Bu sayede uygulama telemetry "kapalı" durumdayken bile cost tracking yapmaya devam eder — admin paneli `/api/telemetry/cost` çalışmaya devam eder.

---

## OTLP exporter

```csharp
private static void ConfigureOtlp(OtlpExporterOptions otlpOptions, TelemetryOptions.OtlpExporterOptions src)
{
    otlpOptions.Endpoint = new Uri(src.Endpoint!);
    otlpOptions.Protocol = src.Protocol?.ToLowerInvariant() switch
    {
        "httpprotobuf" or "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
        _ => OtlpExportProtocol.Grpc
    };
    if (!string.IsNullOrWhiteSpace(src.Headers))
        otlpOptions.Headers = src.Headers;
}
```

| Protocol değeri | Sonuç |
|---|---|
| `"grpc"` veya boş | `OtlpExportProtocol.Grpc` (port 4317) |
| `"httpprotobuf"`, `"http/protobuf"` | `OtlpExportProtocol.HttpProtobuf` (port 4318) |

`Headers`: Authentication header'ları (örn. `"Authorization=Bearer xxx"`). Production'da SaaS observability sağlayıcısı (Honeycomb, Grafana Cloud) için.

### OTLP yoksa?

`options.Otlp.Endpoint` boşsa exporter eklenmez — span/metric üretilir ama bir yere gönderilmez. Lokal development için (Jaeger çalıştırmadan) kullanışlı.

---

## Otomatik instrumentation

| Instrumentation | Yakalanan span'ler |
|---|---|
| `AspNetCoreInstrumentation` | HTTP request başına span (`{method} {path}`) |
| `HttpClientInstrumentation` | `HttpClient.SendAsync` çağrıları |
| `EntityFrameworkCoreInstrumentation` | Her SQL sorgusu (sadece tracing'de) |

Bu instrumentation'lar bizim kodumuzu değiştirmeden çalışır — kütüphaneler kendi ActivitySource'larını expose eder, OpenTelemetry SDK onları dinler.

---

## Service Identity

```csharp
.ConfigureResource(r => r.AddService(options.ServiceName, serviceVersion: options.ServiceVersion))
```

OTLP'ye giden tüm span/metric'lere `resource.service.name` ve `resource.service.version` etiketleri eklenir. Grafana/Jaeger'da farklı uygulamaları ayırt etmek için:

```
service.name=CustomerSupportBot.Api        ← bu uygulama
service.name=AnotherService                ← başka uygulama
```

---

## TelemetryChatClient kaydı (bu extension dışı!)

**Önemli:** `TelemetryChatClient` bu extension'da kayıt edilmez. Onun kaydı **Composition Root**'ta — `CustomerSupportBot.Api/Extensions/AiServicesExtensions.cs` (`AddAiServices`, `Program.cs` tarafından çağrılır) — yapılır, çünkü hangi `IChatClient`'ı sarmaladığı ancak orada bilinir. `Adapters.AI` sadece asıl client'ı oluşturur; `Adapters.Telemetry` decorator sınıfını sağlar; ikisini birleştirme sorumluluğu Api katmanındadır. Ayrıca `TelemetryOptions.Enabled == false` ise sarmalama hiç yapılmaz, ham `IChatClient` döner.

Tipik kayıt (bkz. [CustomerSupportBot.Adapters.AI/DependencyInjection.md](../CustomerSupportBot.Adapters.AI/DependencyInjection.md) — birebir güncel kod için):

```csharp
// Api/Extensions/AiServicesExtensions.cs (Composition Root)
services.AddSingleton<IChatClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    var inner = AiClientFactory.CreateStandardChatClient(options);
    return WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());
});

private static IChatClient WrapWithTelemetry(IServiceProvider sp, IChatClient inner, string modelHint, string provider)
{
    var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
    if (!telemetryOptions.Enabled) return inner;

    return new TelemetryChatClient(
        inner: inner,
        costCalculator: sp.GetRequiredService<ICostCalculatorPort>(),
        usageStore: sp.GetRequiredService<CostUsageStore>(),
        modelHint: modelHint,
        provider: provider,
        logger: sp.GetRequiredService<ILogger<TelemetryChatClient>>(),
        persistence: sp.GetService<ILlmCallPersistencePort>()  // Optional
    );
}
```

`Adapters.Telemetry` kütüphaneyi sağlar; `Adapters.AI` asıl client'ı üretir; Api katmanı ikisini birleştirir.

---

## Bağlantılar

- [TelemetryOptions.md](TelemetryOptions.md) — yapılandırma alanları
- [CustomerSupportTelemetry.md](CustomerSupportTelemetry.md) — ActivitySource/Meter detayları
