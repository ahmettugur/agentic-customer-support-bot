# CustomerSupportTelemetry

**Dosya:** `OpenTelemetry/CustomerSupportTelemetry.cs`  
**Tür:** `public static class`

Uygulama genelinde **paylaşılan** OpenTelemetry primitif'leri:
- Tek `ActivitySource` (tüm span'lerin kaynağı)
- Tek `Meter` (tüm metric'lerin kaynağı)
- Counter + Histogram tanımları
- Activity helper metodları

---

## ActivitySource & Meter

```csharp
public const string ActivitySourceName = TelemetryConstants.ActivitySourceName;  // "CustomerSupportBot"
public const string MeterName = TelemetryConstants.MeterName;                    // "CustomerSupportBot"

public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
public static readonly Meter Meter = new(MeterName, "1.0.0");
```

**Bir tane.** Tüm uygulama bu tek instance üzerinden span/metric yayar.

`AddTelemetryAdapters` extension'ı OpenTelemetry pipeline'ını bu isimleri dinleyecek şekilde yapılandırır:

```csharp
tracing.AddSource("CustomerSupportBot");
metrics.AddMeter("CustomerSupportBot");
```

---

## Counter ve Histogram'lar

OpenTelemetry semantic convention'larına yakın isimlendirme:

### LLM metric'leri

| Field | Type | İsim | Birim |
|---|---|---|---|
| `LlmCallsCounter` | `Counter<long>` | `ai.llm.calls` | `{call}` |
| `InputTokensCounter` | `Counter<long>` | `ai.tokens.input` | `{token}` |
| `OutputTokensCounter` | `Counter<long>` | `ai.tokens.output` | `{token}` |
| `CostUsdCounter` | `Counter<double>` | `ai.cost.usd` | `USD` |
| `LlmLatencyHistogram` | `Histogram<double>` | `ai.llm.duration` | `ms` |

`TelemetryChatClient` her LLM çağrısında bu counter'lara ekleme yapar.

### Agent / workflow metric'leri

| Field | Type | İsim | Birim |
|---|---|---|---|
| `ToolInvocationsCounter` | `Counter<long>` | `agent.tool.invocations` | `{call}` |
| `WorkflowDurationHistogram` | `Histogram<double>` | `agent.workflow.duration` | `ms` |
| `WorkflowCompletionsCounter` | `Counter<long>` | `agent.workflow.completions` | `{run}` |

`Adapters.Agents` katmanındaki tool ve agent kodu bunları kullanır.

### Counter<long> vs Counter<double>

- `long` → Sayım: kaç çağrı, kaç token
- `double` → Sürekli değer: USD (decimal precision OTLP'de gerekmez)

---

## Activity helper'lar

Standart pattern: span başlat → tag ekle → using ile dispose et.

### `StartLlmActivity`

```csharp
public static Activity? StartLlmActivity(string operation, string? model = null, string? provider = null)
{
    var activity = ActivitySource.StartActivity($"ai.{operation}", ActivityKind.Client);
    if (activity == null) return null;
    if (!string.IsNullOrEmpty(model))    activity.SetTag("ai.model", model);
    if (!string.IsNullOrEmpty(provider)) activity.SetTag("ai.provider", provider);
    return activity;
}
```

- **Kind:** `Client` — dış servise (LLM API) outbound çağrı
- **İsim:** `ai.chat`, `ai.chat.stream`, `ai.embedding`, ...
- **Tag'ler:** ai.model, ai.provider, sonradan ai.tokens.*, ai.cost.usd, ai.duration.ms

### `StartAgentActivity`

```csharp
public static Activity? StartAgentActivity(string agentName, string? sessionId = null, string? traceId = null)
{
    var activity = ActivitySource.StartActivity($"agent.{agentName}", ActivityKind.Internal);
    activity?.SetTag("agent.name", agentName);
    activity?.SetTag("session.id", sessionId);
    activity?.SetTag("trace.id", traceId);
    return activity;
}
```

- **Kind:** `Internal` — process içinde iş
- **İsim:** `agent.PlanningAgent`, `agent.OrderAgent`, ...
- **Tag'ler:** agent.name, session.id, trace.id

### `StartToolActivity`

```csharp
public static Activity? StartToolActivity(string toolName, string? sessionId = null)
{
    var activity = ActivitySource.StartActivity($"tool.{toolName}", ActivityKind.Internal);
    activity?.SetTag("tool.name", toolName);
    activity?.SetTag("session.id", sessionId);
    return activity;
}
```

- **İsim:** `tool.order_status_tool`, `tool.product_inquiry_tool`, ...

---

## Span hiyerarşisi örneği

Bir kullanıcı turn'ünde tipik span ağacı:

```
HTTP POST /api/chat                                   [Server]
├── agent.PlanningAgent                               [Internal]
│   └── ai.chat                                       [Client]
│       └── HTTP POST api.openai.com                  [Client]  (HttpClient instrumentation)
├── agent.OrderAgent                                  [Internal]
│   ├── ai.chat                                       [Client]
│   │   └── HTTP POST api.openai.com                  [Client]
│   └── tool.order_status_tool                        [Internal]
│       └── EF Core: SELECT FROM orders               [Client]  (EF Core instrumentation)
└── agent.ResponseAgent                               [Internal]
    └── ai.chat                                       [Client]
```

Jaeger/Tempo'da bu ağaç görsel timeline olarak gösterilir — bottleneck'leri anında görebilirsin.

---

## `Activity?` neden nullable?

```csharp
var activity = ActivitySource.StartActivity(...);
if (activity == null) return null;
```

`StartActivity` `null` döner eğer:
- Hiçbir listener dinlemiyorsa (ör. tracing kapalıysa)
- Sampling kararı "drop" verirse (yüksek hacimde sampling)

`null` dönmek **bir tasarım kararı** — sampling decision burada yapılır, gereksiz activity oluşturulmaz (perf optimizasyonu).

`activity?.SetTag(...)` ile nullable-safe çağrılır.

---

## Tag (Attribute) konvansiyonları

OpenTelemetry semantic convention'larına yakın:

| Tag | Örnek değer | Anlamı |
|---|---|---|
| `ai.model` | `gpt-4o-mini` | Kullanılan model |
| `ai.provider` | `openai` | Provider |
| `ai.tokens.input` | `1234` | Input token sayısı |
| `ai.tokens.output` | `567` | Output token sayısı |
| `ai.cost.usd` | `0.000789` | Hesaplanan maliyet |
| `ai.duration.ms` | `1450.23` | Çağrı süresi |
| `agent.name` | `OrderAgent` | Agent adı |
| `tool.name` | `order_status_tool` | Tool adı |
| `session.id` | `sess-abc-123` | Session ID |
| `trace.id` | `trace-xyz-789` | Reasoning trace ID |
| `error.type` | `System.TimeoutException` | Exception tipi (FAIL durumunda) |

---

## OpenTelemetry semantic convention uyumu

Resmi convention bazı isimler kullanır:
- `gen_ai.system` (provider)
- `gen_ai.request.model` (model)
- `gen_ai.usage.input_tokens`
- `gen_ai.usage.output_tokens`

Bu proje `ai.*` prefix'ini tercih etti (daha kısa). Production'da Grafana dashboard'ları bu prefix'e göre kurulmalı. İleride semantic convention'a tam uyum için isim değişikliği yapılabilir — tek nokta burası.

---

## Statik kullanım pattern'i

`CustomerSupportTelemetry` static class olduğu için DI yok:

```csharp
// Her yerden direkt:
using (var activity = CustomerSupportTelemetry.StartLlmActivity("chat", "gpt-4o", "openai"))
{
    var response = await client.GetResponseAsync(...);
    activity?.SetTag("ai.tokens.input", response.Usage?.InputTokenCount);
    CustomerSupportTelemetry.LlmCallsCounter.Add(1);
}
```

Static state thread-safe — ActivitySource, Meter, Counter, Histogram hepsi internal locking yapar.

---

## Test edilebilirlik

OpenTelemetry test SDK ile span'leri yakalayabilirsin:

```csharp
var exportedActivities = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "CustomerSupportBot",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = exportedActivities.Add
};
ActivitySource.AddActivityListener(listener);

// İşlem yap...
using (var a = CustomerSupportTelemetry.StartLlmActivity("chat")) { }

Assert.Single(exportedActivities);
Assert.Equal("ai.chat", exportedActivities[0].OperationName);
```
