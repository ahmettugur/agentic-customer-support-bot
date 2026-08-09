# TracePortService

**Dosya:** `Services/Telemetry/TracePortService.cs`  
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

Tüm metodlar **senkrondur** (`Task` yok, `CancellationToken` almazlar).

### `GetRecentTraces`

```csharp
IReadOnlyList<ReasoningTrace> GetRecentTraces(int count = 20)
```

Son `count` trace kaydını döner (varsayılan **20**).

---

### `GetTrace`

```csharp
ReasoningTrace? GetTrace(string traceId)
```

Tekil trace'i döner.

---

### `GetTracesBySession`

```csharp
IReadOnlyList<ReasoningTrace> GetTracesBySession(string sessionId)
```

Belirli session'ın tüm trace'lerini döner.

---

### `GetSessionsSummary`

```csharp
IReadOnlyList<TracedSessionSummary> GetSessionsSummary()
```

Parametre almaz — dahili olarak en son 500 trace taranarak session bazlı özet çıkarılır.

```csharp
public sealed record TracedSessionSummary(
    string SessionId,
    string Title,
    int TraceCount,
    DateTime LastTraceAt,
    string? LastQuery,
    int MessageCount);
```

`MessageCount` trace sayısından türetilmez — `ISessionManager.GetHistory(sessionId).Count` ile gerçek mesaj geçmişinden okunur.

---

### `GetStats`

```csharp
TraceStatsSummary GetStats()
```

Son 500 trace üzerinden istatistik hesaplar.

```csharp
public sealed record TraceStatsSummary(
    int TotalTraces,
    int CompletedCount,
    int ErrorCount,
    double AvgDurationMs,
    double AvgIterationCount,
    IReadOnlyDictionary<string, int> TerminationReasons);
```

`CompletedCount` ham bir sayıdır — hazır bir `CompletionRate` oranı **yoktur**; gerekiyorsa `CompletedCount / (double)TotalTraces` çağıran tarafta hesaplanır.

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
    ReasoningResult? Reasoning;   // Intent, sanity issues, confidence — intent'in tek sahibi
    PlanningResult? Planning;     // SelectedAgent, routing (intent tespiti YOK)
    List<AgentVisit> AgentVisits; // Ziyaret edilen agent'lar ve süreleri
}
```

---

## API endpoint'leri

Bkz. [Endpoints-Observability.md](../api/Endpoints-Observability.md) — gerçek route'lar için (`Admin` yetkisi gerektirir).
