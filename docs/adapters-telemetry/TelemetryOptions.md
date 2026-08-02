# TelemetryOptions

**Dosyalar:**
- `Options/TelemetryOptions.cs` ← **aktif** yapılandırma modeli
- `Models/TelemetryAdapterOptions.cs` ← legacy (kullanılmıyor)

`appsettings.json > "Telemetry"` bölümünden bind edilir.

---

## Yapı

```csharp
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "CustomerSupportBot.Api";
    public string ServiceVersion { get; set; } = "1.0.0";
    public OtlpExporterOptions Otlp { get; set; } = new();
    public bool TracingEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
    public Dictionary<string, ModelPricing> Pricing { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public sealed class OtlpExporterOptions
    {
        public string? Endpoint { get; set; }
        public string Protocol { get; set; } = "grpc";
        public string? Headers { get; set; }
    }

    public sealed class ModelPricing
    {
        public decimal InputPer1K { get; set; }
        public decimal OutputPer1K { get; set; }
    }
}
```

---

## Alanlar

| Alan | Default | Açıklama |
|---|---|---|
| `Enabled` | `true` | Master switch — false ise OpenTelemetry hiç kurulmaz |
| `ServiceName` | `"CustomerSupportBot.Api"` | OTLP `resource.service.name` |
| `ServiceVersion` | `"1.0.0"` | OTLP `resource.service.version` |
| `TracingEnabled` | `true` | Span yayını |
| `MetricsEnabled` | `true` | Metric yayını |
| `Otlp.Endpoint` | `null` | Boşsa exporter yok (sadece process içinde) |
| `Otlp.Protocol` | `"grpc"` | `"grpc"` veya `"httpprotobuf"` |
| `Otlp.Headers` | `null` | Auth header'ları |
| `Pricing` | Boş Dict | Model → USD/1K token |

---

## Pricing tablosu

Maliyet hesaplaması için kritik bölüm:

```json
{
  "Telemetry": {
    "Pricing": {
      "gpt-4o-mini":    { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
      "gpt-4o":         { "InputPer1K": 0.005,   "OutputPer1K": 0.015 },
      "openai_default": { "InputPer1K": 0.001,   "OutputPer1K": 0.002 },
      "default":        { "InputPer1K": 0.0005,  "OutputPer1K": 0.001 }
    }
  }
}
```

### Lookup öncelik sırası

`CostCalculator.CalculateCost(model, provider, ...)` şu sırayı dener:

1. **Model adıyla tam eşleşme** — `"gpt-4o-mini"`
2. **Provider default** — `"{provider}_default"` (örn. `"openai_default"`)
3. **Global default** — `"default"`
4. Yoksa → `0` döner (maliyet hesaplanmaz, ama metric/span yine de yayılır)

### Case-insensitive

```csharp
Pricing = new(StringComparer.OrdinalIgnoreCase);
```

`"GPT-4o-mini"` ile `"gpt-4o-mini"` aynı pricing'i bulur — LLM provider'ları farklı casing kullanabilir.

### Güncel fiyatlar

⚠️ Fiyatlar **manuel güncellenmelidir**. OpenAI fiyat değiştirirse `appsettings.json` güncellenir; kod değişmez. Üretimde environment variable / Azure App Configuration ile yönetilebilir.

Tahmini güncel fiyatlar (2025-01):

| Model | InputPer1K | OutputPer1K |
|---|---|---|
| `gpt-4o-mini` | 0.00015 | 0.0006 |
| `gpt-4o` | 0.0025 | 0.01 |
| `gpt-4-turbo` | 0.01 | 0.03 |

---

## Örnek tam yapılandırma

```json
{
  "Telemetry": {
    "Enabled": true,
    "ServiceName": "CustomerSupportBot.Api",
    "ServiceVersion": "1.0.0",
    "TracingEnabled": true,
    "MetricsEnabled": true,
    "Otlp": {
      "Endpoint": "http://localhost:4317",
      "Protocol": "grpc",
      "Headers": null
    },
    "Pricing": {
      "gpt-4o-mini": { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
      "default":     { "InputPer1K": 0.001,   "OutputPer1K": 0.002 }
    }
  }
}
```

### Production override (env var)

```bash
Telemetry__Otlp__Endpoint=https://otlp.honeycomb.io
Telemetry__Otlp__Protocol=httpprotobuf
Telemetry__Otlp__Headers=x-honeycomb-team=abc123
```

`__` ASP.NET Core'da `.` yerine geçer (Linux env var compat).

---

## TelemetryAdapterOptions (`Models/TelemetryAdapterOptions.cs`)

`Models/TelemetryAdapterOptions.cs` projede mevcut bir sınıftır. `TelemetryAdapterServiceCollectionExtensions` bu sınıfı DI bağlaması için **kullanmaz** — `Options/TelemetryOptions.cs` kullanılır. Her iki sınıfın yapısı (Enabled, TracingEnabled, MetricsEnabled, ServiceName, ServiceVersion, Otlp, Pricing) aynıdır.

**DI bağlaması için her zaman `TelemetryOptions`'ı tercih et.** `TelemetryAdapterOptions` doğrudan tüketilmemelidir; ileride kaldırılabilir.
