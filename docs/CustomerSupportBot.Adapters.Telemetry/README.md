# CustomerSupportBot.Adapters.Telemetry

**Observability** katmanı — LLM çağrılarını ölçer, maliyeti hesaplar, span/metric yayını yapar.

İki sorumluluk:
1. **OpenTelemetry pipeline'ı** kurmak — distributed tracing + metrics
2. **LLM maliyet/kullanım** takibi — model bazlı token + USD aggregation

---

## Klasör yapısı

```
CustomerSupportBot.Adapters.Telemetry/
├── Chat/
│   └── TelemetryChatClient.cs        ← IChatClient decorator (LLM intercept)
├── DependencyInjection/
│   └── TelemetryAdapterServiceCollectionExtensions.cs
├── HealthChecks/                      (yok)
├── Models/
│   └── TelemetryAdapterOptions.cs    ← (DI'da kullanılmıyor; TelemetryOptions tercih edilir)
├── OpenTelemetry/
│   ├── CostCalculator.cs             ← ICostCalculatorPort
│   ├── CostUsageStore.cs             ← ICostUsageStorePort
│   └── CustomerSupportTelemetry.cs   ← ActivitySource + Meter (statik)
└── Options/
    └── TelemetryOptions.cs           ← Aktif yapılandırma modeli
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
|---|---|
| [DependencyInjection.md](DependencyInjection.md) | `AddTelemetryAdapters`, OpenTelemetry pipeline kurulumu, OTLP exporter |
| [TelemetryOptions.md](TelemetryOptions.md) | Yapılandırma modeli + pricing tablosu |
| [TelemetryChatClient.md](TelemetryChatClient.md) | IChatClient decorator — LLM çağrı intercept, span + metric |
| [CostCalculator.md](CostCalculator.md) | Maliyet hesaplama (per-model, provider default, global fallback) |
| [CostUsageStore.md](CostUsageStore.md) | In-memory toplam kullanım istatistikleri |
| [CustomerSupportTelemetry.md](CustomerSupportTelemetry.md) | Merkezi ActivitySource + Meter + counter/histogram katalogu |

---

## Port → Adapter eşlemesi

| Port (Application) | Adapter |
|---|---|
| `ICostCalculatorPort` | `CostCalculator` |
| `ICostUsageStorePort` | `CostUsageStore` |

Diğer dosyalar **port implementasyonu değil** — altyapı yardımcıları (decorator, statik telemetry primitives, DI extension).

---

## Metric kataloğu

`CustomerSupportTelemetry` tarafından tanımlı OpenTelemetry metric'leri:

| Metric | Tip | Birim | Tag'ler |
|---|---|---|---|
| `ai.llm.calls` | Counter<long> | `{call}` | `ai.model`, `ai.provider` |
| `ai.tokens.input` | Counter<long> | `{token}` | `ai.model`, `ai.provider` |
| `ai.tokens.output` | Counter<long> | `{token}` | `ai.model`, `ai.provider` |
| `ai.cost.usd` | Counter<double> | `USD` | `ai.model`, `ai.provider` |
| `ai.llm.duration` | Histogram<double> | `ms` | `ai.model`, `ai.provider` |
| `agent.tool.invocations` | Counter<long> | `{call}` | `tool.name`, `session.id` |
| `agent.workflow.duration` | Histogram<double> | `ms` | — |
| `agent.workflow.completions` | Counter<long> | `{run}` | `status` |

---

## Span kataloğu

`ActivitySource = "CustomerSupportBot"`:

| Span | Kind | Açan | Tag'ler |
|---|---|---|---|
| `ai.chat` | Client | TelemetryChatClient | `ai.model`, `ai.provider`, `ai.tokens.input/output`, `ai.cost.usd`, `ai.duration.ms` |
| `ai.chat.stream` | Client | TelemetryChatClient | Aynı (streaming) |
| `agent.{name}` | Internal | Application agents | `agent.name`, `session.id`, `trace.id` |
| `tool.{name}` | Internal | Specialist tools | `tool.name`, `session.id` |

Ek olarak otomatik instrumentation'lar: `AspNetCore`, `HttpClient`, `EntityFrameworkCore`.

---

## Akış: Bir LLM çağrısı

```
Specialist agent → IChatClient.GetResponseAsync()
   ↓
TelemetryChatClient (decorator) intercept eder
   ├─ Activity başlat: "ai.chat" [Client kind]
   ├─ Stopwatch başlat
   ↓
Asıl IChatClient (OpenAI/Azure adapter)
   ↓
   Response döner — UsageDetails ile (input + output tokens)
   ↓
TelemetryChatClient.RecordSuccess():
   ├─ CostCalculator.CalculateCost(model, tokens) → USD
   ├─ Meter counter'ları update: calls, tokens, cost
   ├─ Histogram: duration
   ├─ CostUsageStore.Record(...) — in-memory aggregate
   ├─ Activity tag'leri set + Status=Ok
   ├─ (opsiyonel) ILlmCallPersistencePort → fire-and-forget DB INSERT
   ↓
OpenTelemetry SDK → OTLP exporter → Jaeger/Prometheus/Grafana
```

---

## OpenTelemetry vs lokal store

Bu adapter **iki paralel kanal** kullanır:

| Kanal | Veri | Amaç |
|---|---|---|
| OpenTelemetry (OTLP) | Counter/Histogram + span | Production observability (Grafana/Jaeger) |
| `CostUsageStore` (in-memory) | Per-model aggregate | Demo/admin paneli (`/api/telemetry/cost`) |

Production'da `CostUsageStore` yerine Prometheus query API kullanılabilir; ama "tek tıkla görsel maliyet" için lokal store pratik.

---

## Bağımlılıklar

| Paket | Amaç |
|---|---|
| `OpenTelemetry` | Core API |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP exporter |
| `OpenTelemetry.Instrumentation.AspNetCore` | HTTP request tracing |
| `OpenTelemetry.Instrumentation.Http` | HttpClient çağrıları |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | EF Core SQL tracing |
| `Microsoft.Extensions.AI` | `IChatClient`, `DelegatingChatClient` |

---

## Kullanım

`Program.cs`:

```csharp
services.AddTelemetryAdapters(configuration);
```

`appsettings.json`:

```json
{
  "Telemetry": {
    "Enabled": true,
    "TracingEnabled": true,
    "MetricsEnabled": true,
    "ServiceName": "CustomerSupportBot.Api",
    "ServiceVersion": "1.0.0",
    "Otlp": {
      "Endpoint": "http://localhost:4317",
      "Protocol": "grpc"
    },
    "Pricing": {
      "gpt-4o-mini": { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
      "gpt-4o":      { "InputPer1K": 0.005,   "OutputPer1K": 0.015 },
      "default":     { "InputPer1K": 0.001,   "OutputPer1K": 0.002 }
    }
  }
}
```
