# Telemetry — Observability ve Maliyet Takibi

Bu doküman OpenTelemetry entegrasyonunu, maliyet muhasebesi sistemini ve gözlemleme altyapısını anlatır.

---

## 1. Genel Bakış

Uygulama tüm LLM çağrılarını, ajan adımlarını, tool çağrılarını ve workflow turlarını **OpenTelemetry** üzerinden izler. Ek olarak her LLM çağrısının token kullanımı ve **USD maliyeti** hesaplanır.

```
CustomerSupportBot
    │
    ├─ TelemetryChatClient (IChatClient DelegatingChatClient wrapper)
    │   └─ Her LLM çağrısı → span + token + USD maliyet
    │
    ├─ Agent OpenTelemetry middleware (.UseOpenTelemetry)
    │   └─ Her agent.run ve tool çağrısı → span
    │
    ├─ ASP.NET Core / HttpClient / EF Core instrumentation
    │
    └── OTLP Exporter ──▶ Jaeger v2 (OTLP receiver) ──▶ Elasticsearch
```

---

## 2. Konfigürasyon

```json
{
  "Telemetry": {
    "Enabled": true,
    "ServiceName": "CustomerSupportBot",
    "ServiceVersion": "1.0.0",
    "TracingEnabled": true,
    "MetricsEnabled": true,
    "Otlp": {
      "Endpoint": "http://localhost:4317",
      "Protocol": "grpc",
      "Headers": ""
    },
    "Pricing": {
      "default":                { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
      "gpt-5.4":                { "InputPer1K": 0.0025,  "OutputPer1K": 0.01 },
      "gpt-5.4-nano":           { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
      "text-embedding-3-large": { "InputPer1K": 0.00013, "OutputPer1K": 0 },
      "claude-haiku-4-5":       { "InputPer1K": 0.001,   "OutputPer1K": 0.005 }
    }
  }
}
```

| Ayar | Açıklama |
|------|----------|
| `Enabled` | `false` → tüm telemetri pipeline'ı devre dışı |
| `TracingEnabled` | Span yayımı |
| `MetricsEnabled` | Metric yayımı |
| `Otlp.Endpoint` | Boş bırakılırsa exporter eklenmez (sadece in-process maliyet) |
| `Otlp.Protocol` | `grpc` veya `httpprotobuf` |
| `Pricing` | Model adı → USD/1K token mapping. Bilinmeyen model `default` fiyatını kullanır |

---

## 3. Span Hiyerarşisi

`ActivitySource = "CustomerSupportBot"` altında yayılan span'ler:

```
POST /chat/stream                          (ASP.NET Core instrumentation)
├─ ai.chat.stream                          ← ReasoningChatClient
│   tags: ai.model, ai.provider, ai.tokens.input/output, ai.cost.usd, ai.duration.ms
├─ agent.PlanningAgent
│   tags: agent.name, session.id, trace.id
├─ agent.OrderAgent
│   ├─ ai.chat                             ← standart IChatClient
│   └─ tool.order_status_tool
│       tags: tool.name, session.id
├─ agent.ResponseAgent
│   └─ ai.chat
└─ (opsiyonel) db.query                    (EF Core instrumentation)
```

### Span Tag'leri

| Tag | Açıklama |
|-----|----------|
| `ai.model` | Kullanılan model adı (ör. `gpt-5.4`) |
| `ai.provider` | Sağlayıcı (OpenAI, AzureOpenAI, Anthropic) |
| `ai.tokens.input` | Input token sayısı |
| `ai.tokens.output` | Output token sayısı |
| `ai.cost.usd` | Bu çağrının USD maliyeti |
| `ai.duration.ms` | Çağrı süresi (ms) |
| `agent.name` | Ajan adı |
| `tool.name` | Tool adı |
| `session.id` | Oturum ID |
| `trace.id` | Reasoning trace ID |

---

## 4. Metric'ler

`Meter = "CustomerSupportBot"` altında yayılan metric'ler:

| Metric | Tip | Birim | Açıklama |
|--------|-----|-------|----------|
| `ai.llm.calls` | Counter | `{call}` | Toplam LLM çağrı sayısı |
| `ai.tokens.input` | Counter | `{token}` | Toplam input token |
| `ai.tokens.output` | Counter | `{token}` | Toplam output token |
| `ai.cost.usd` | Counter | `USD` | Toplam maliyet |
| `ai.llm.duration` | Histogram | `ms` | LLM çağrı süre dağılımı |
| `agent.tool.invocations` | Counter | `{call}` | Tool çağrı sayısı |
| `agent.workflow.duration` | Histogram | `ms` | Workflow süre dağılımı |
| `agent.workflow.completions` | Counter | `{run}` | Tamamlanan workflow sayısı |

Ek olarak **ASP.NET Core**, **HttpClient** ve **EF Core** instrumentation otomatik etkindir.

---

## 5. TelemetryChatClient

`IChatClient`'ı saran `DelegatingChatClient` wrapper'ı. Her LLM çağrısında:

1. `ActivitySource`'tan span açar
2. Model adı, provider bilgisi tag olarak eklenir
3. Çağrı tamamlanınca token sayımı yapılır (streaming'de `UsageContent` dahil)
4. `ICostCalculator` ile USD maliyet hesaplanır
5. `CostUsageStore`'a model bazlı agregat yazılır

`Telemetry.Enabled = false` ise `IChatClient` doğrudan (wrapper'sız) kullanılır.

---

## 6. Maliyet Takibi (CostUsageStore)

OTLP exporter ayağa kaldırılmasa bile maliyet bilgisi her zaman in-memory tutulur.

### Admin Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/telemetry/cost` | `GET` | Model bazlı toplam token + USD maliyet özeti |
| `/telemetry/cost/models` | `GET` | Fiyat tablosunda tanımlı bilinen modeller |
| `/telemetry/cost/reset` | `POST` | In-memory maliyet sayaçlarını sıfırlar |

### Yanıt Örneği

```json
{
  "totalCalls": 142,
  "totalInputTokens": 184320,
  "totalOutputTokens": 56204,
  "totalCostUsd": 1.0473,
  "byModel": [
    {
      "model": "gpt-5.4",
      "calls": 88,
      "inputTokens": 165000,
      "outputTokens": 51000,
      "costUsd": 0.9225,
      "averageLatencyMs": 1820
    }
  ]
}
```

---

## 7. Altyapı Stack

### Jaeger v2

Uygulama **Jaeger v2** (2.17.0+) kullanır. Jaeger v2, OpenTelemetry Collector formatında YAML konfigürasyon dosyası ile yapılandırılır ve OTLP receiver'ı kendi bünyesinde barındırır.

```yaml
# docker-compose.yml servisleri
jaeger:          # jaegertracing/jaeger:2.17.0 — Jaeger v2 (OTLP + UI)
                 # http://localhost:16686 — Jaeger UI
                 # 4317 (gRPC), 4318 (HTTP) — OTLP receiver
elasticsearch:   # Jaeger backend storage
kibana:          # http://localhost:5601 — Elasticsearch görselleştirme
```

Konfigürasyon dosyası: `jaeger-v2-config.yaml` (repo kökünde). Jaeger v2 artık environment variable yerine bu YAML dosyası üzerinden yapılandırılır.

### OTel Collector (Opsiyonel)

Jaeger v2 doğrudan OTLP alabildiği için OTel Collector artık opsiyoneldir. `docker compose --profile otel up` ile açılabilir. Host portları çakışmayı önlemek için `4327` (gRPC) ve `4328` (HTTP) olarak ayarlanmıştır.

### Kurulum

```powershell
docker compose up -d elasticsearch jaeger
dotnet run --project CustomerSupportBot
# Jaeger UI: http://localhost:16686 → Service: CustomerSupportBot
```

---

## 8. Sorun Giderme

| Belirti | Sebep | Çözüm |
|---------|-------|-------|
| Jaeger'da span görünmüyor | `Otlp.Endpoint` boş veya yanlış | `http://localhost:4317` olarak ayarla |
| Maliyet 0 görünüyor | Model pricing tablosunda tanımsız | `Pricing` bölümüne model ekle |
| Span eksik (agent span yok) | Agent OpenTelemetry wrap edilmemiş | `WrapWithTelemetry` çağrısını kontrol et |
| Jaeger başlamıyor | Konfigürasyon dosyası eksik | `jaeger-v2-config.yaml` dosyasının repo kökünde olduğundan emin ol |

---

## Çapraz Referanslar

- **Konfigürasyon detayları** → [runtime.md](runtime.md#telemetry)
- **Mimari bakış** → [architecture.md](architecture.md)
- **Endpoint referansı** → [api.md](api.md#8-telemetry-endpoints)
