# CustomerSupportTelemetry

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CustomerSupportTelemetry.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.OpenTelemetry`

## Ne işe yarar?

`CustomerSupportTelemetry`, tüm çözüm genelinde paylaşılan tekil `ActivitySource` ("CustomerSupportBot") ve `Meter` ("CustomerSupportBot") nesnelerini barındıran; LLM çağrıları, araç çalıştırmaları, workflow süreleri ve maliyet sayaçlarını OpenTelemetry semantik standartlarında yöneten merkezi telemetri sınıfıdır.

## Hangi amaçla kullanılır`?

- Dağıtık izleme için hiyerarşik `Activity` (span) başlatmak (`StartAgentActivity`, `StartToolActivity`, `StartLlmActivity`).
- Sistem genelinde gerçekleşen çağrıları, harcanan token'ları ve maliyetleri counter ve histogram metrikleri olarak toplamak.

## Sorumlulukları

- **Üstlendiği:**
  - `ActivitySource` ve `Meter` nesnelerini "CustomerSupportBot" (1.0.0) sürümüyle sunmak.
  - Metrik sayaçlarını (`LlmCallsCounter`, `InputTokensCounter`, `OutputTokensCounter`, `CostUsdCounter`, `ToolInvocationsCounter`, `WorkflowCompletionsCounter`) tanımlamak.
  - Gecikme ve süre histogramlarını (`LlmLatencyHistogram`, `WorkflowDurationHistogram`) tanımlamak.
  - Span başlatıcı yardımcı metotları sunmak.

## Tanımlı Metrikler

| Metrik Adı | Tür | Birim | Açıklama |
|---|---|---|---|
| `ai.llm.calls` | `Counter<long>` | `{call}` | Toplam LLM (chat/reasoning) çağrı sayısı. |
| `ai.tokens.input` | `Counter<long>` | `{token}` | Toplam girdi (prompt) token sayısı. |
| `ai.tokens.output` | `Counter<long>` | `{token}` | Toplam çıktı (completion) token sayısı. |
| `ai.cost.usd` | `Counter<double>` | `USD` | Tahmini toplam LLM maliyeti. |
| `ai.llm.duration` | `Histogram<double>` | `ms` | LLM çağrılarının yanıt gecikmesi. |
| `agent.tool.invocations` | `Counter<long>` | `{call}` | Domain araçlarının çağrılma sayısı. |
| `agent.workflow.duration` | `Histogram<double>` | `ms` | Uçtan uca iş akışı tamamlanma süresi. |
| `agent.workflow.completions` | `Counter<long>` | `{run}` | Başarıyla veya hatayla biten iş akışı sayısı. |

## Metotlar ve İç Çalışma Mantıkları

### 1. `StartAgentActivity`
```csharp
public static Activity? StartAgentActivity(
    string agentName,
    string? sessionId = null,
    string? traceId = null)
```
- **Ne işe yarar?:** Bir ajanın çalışması için `agent.{agentName}` adında `ActivityKind.Internal` span'i başlatır; `sessionId` ve `traceId` etiketlerini ekler.

### 2. `StartToolActivity`
```csharp
public static Activity? StartToolActivity(
    string toolName,
    string? sessionId = null)
```
- **Ne işe yarar?:** Bir araç çağrısı için `tool.{toolName}` adında `ActivityKind.Internal` span'i başlatır.

### 3. `StartLlmActivity`
```csharp
public static Activity? StartLlmActivity(
    string operation,
    string? model = null,
    string? provider = null)
```
- **Ne işe yarar?:** Bir LLM çağrısı için `ai.{operation}` adında `ActivityKind.Client` span'i başlatır; `ai.model` ve `ai.provider` etiketlerini ekler.

## Bağımlılıklar

- `System.Diagnostics.ActivitySource`
- `System.Diagnostics.Metrics.Meter`
- `CustomerSupportBot.Application.Ports.Outbound.Observability.TelemetryConstants`
