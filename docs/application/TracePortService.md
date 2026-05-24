# TracePortService

**Dosya:** `Services/TracePortService.cs`  
**Implements:** `ITracePort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Reasoning trace kayıtlarını sorgular ve istatistik üretir. `IReasoningTraceStore` driven port'unun API katmanına açık görünümüdür.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IReasoningTraceStore` | Trace kayıt ve sorgulama deposu |

---

## Metodlar

### `GetRecentTracesAsync`

```csharp
Task<IReadOnlyList<ReasoningTrace>> GetRecentTracesAsync(int count = 50, CancellationToken ct = default)
```

Son `count` trace kaydını döner.

---

### `GetTraceAsync`

```csharp
Task<ReasoningTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
```

Tekil trace'i döner.

---

### `GetTracesBySessionAsync`

```csharp
Task<IReadOnlyList<ReasoningTrace>> GetTracesBySessionAsync(string sessionId, CancellationToken ct = default)
```

Belirli session'ın tüm trace'lerini döner.

---

### `GetSessionsSummaryAsync`

```csharp
Task<IReadOnlyList<SessionTraceSummary>> GetSessionsSummaryAsync(int count = 50, CancellationToken ct = default)
```

Son `count` session'ın özetini döner.

**Hesaplama:**
```
Son trace'ler → session'a göre grupla
Her group için:
  - SessionId
  - Title: group.First().UserQuery (max 60 karakter)
  - MessageCount: trace sayısı * 2 (her trace = 1 user + 1 bot)
  - LastActivityAt: en son trace'in StartedAt
```

---

### `GetStatsAsync`

```csharp
Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
```

Son 500 trace üzerinden istatistik hesaplar.

**`TraceStats` alanları:**

| Alan | Hesaplama |
|------|----------|
| `TotalTraces` | Son 500 trace sayısı |
| `CompletionRate` | `termination_reason == "completed"` oranı (0.0–1.0) |
| `ErrorCount` | `trace.Error != null` olan sayı |
| `AverageDurationMs` | Ortalama süre (ms) |
| `AverageIterations` | Ortalama iterasyon sayısı |
| `TerminationReasonDistribution` | Her `termination_reason` değerinin frekansı |

---

## ReasoningTrace modeli

```csharp
public class ReasoningTrace
{
    string TraceId;
    string SessionId;
    string UserQuery;
    string? FinalResponse;
    string? TerminationReason;    // "completed", "timeout", "error", "max_iterations"
    string? Error;
    int IterationCount;
    long? DurationMs;
    DateTime StartedAt;
    ReasoningResult? Reasoning;   // Intent, sanity issues, confidence
    PlanningResult? Planning;     // DetectedIntent, agents
    List<AgentVisit> AgentVisits; // Ziyaret edilen agent'lar ve süreleri
}
```

---

## API endpoint'leri

```http
GET /trace/recent?count=50        → GetRecentTracesAsync
GET /trace/{traceId}              → GetTraceAsync
GET /trace/session/{sessionId}    → GetTracesBySessionAsync
GET /trace/sessions-summary       → GetSessionsSummaryAsync
GET /trace/stats                  → GetStatsAsync
```
