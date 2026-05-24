# Trace & Analytics Modelleri

**Dosyalar:**
- `Model/ReasoningTrace.cs`
- `Model/SessionAnalytics.cs`
- `Model/ConversationRating.cs`
- `Model/SlaEvent.cs`

Audit, debug ve analitik için kullanılan modeller.

---

## ReasoningTrace

Bir turn'ün **tam audit kaydı** — debug ve replay için.

```csharp
public sealed class ReasoningTrace
{
    public string TraceId { get; init; }
    public string SessionId { get; init; }
    public string UserQuery { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }

    // Analiz aşamaları
    public ReasoningResult? Reasoning { get; set; }
    public PlanningResult? Planning { get; set; }
    public List<SpecialistReasoning> SpecialistReasonings { get; set; } = new();

    // Timeline
    public List<AgentVisit> AgentVisits { get; set; } = new();
    public List<ToolInvocation> ToolCalls { get; set; } = new();

    // Sonlanma
    public string? TerminationReason { get; set; }      // "completed", "max_messages", "timeout", "terminated_by_restart"
    public string? FinalResponse { get; set; }
    public int IterationCount { get; set; }
    public int EstimatedTokens { get; set; }
    public string? Error { get; set; }
}
```

### Üç aşamalı yazım

| Aşama | Ne yazılır |
|---|---|
| `StartTrace` | Skeleton (TraceId, SessionId, UserQuery, StartedAt) |
| `Update` | Cache update — hot path'te (DB yazma yok) |
| `Complete` | Tam içerik UPDATE (DurationMs, FinalResponse, TerminationReason) |

DB'ye iki yazma: başta INSERT + sonda UPDATE. Update aşamasında cache'e yazılır, ama DB'ye yazılmaz (latency tasarrufu).

### AgentVisit

```csharp
public sealed class AgentVisit
{
    public string AgentName { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Output { get; set; }                  // Truncated (large fields kesilir)
}
```

Agent'ların sıralı çalışma timeline'ı:

```
[PlanningAgent: 230ms] → [OrderAgent: 1400ms] → [ResponseAgent: 180ms]
```

### ToolInvocation

```csharp
public sealed class ToolInvocation
{
    public string ToolName { get; init; }
    public DateTime InvokedAt { get; init; }
    public string AgentName { get; init; }
    public string? ParametersSummary { get; set; }
    public string? ResultSummary { get; set; }
    public bool Success { get; set; }
    public string? Signature { get; set; }               // Hash for dedup detection
}
```

`Signature` aynı tool + aynı parametrelerle iki kez çağrılırsa tespit eder (sanity check için).

### Termination reasons (`WellKnown.Termination`)

| Reason | Anlam |
|---|---|
| `completed` | Normal bitiş (TERMINATE marker) |
| `max_messages` | Maksimum iteration sayısına ulaşıldı |
| `timeout` | Sürede tamamlanamadı |
| `terminated_by_restart` | Uygulama restart olduğunda in-flight |
| `error` | Exception fırlatıldı |

`PersistenceHydrator` startup'ta `CompletedAt is null` olan trace'leri `terminated_by_restart` yapar (orphan kayıt önleme).

---

## SessionAnalytics

Bir session için **özet metrikler** — admin dashboard için.

```csharp
public sealed class SessionAnalytics
{
    public string SessionId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivity { get; init; }

    public int MessageCount { get; set; }
    public int TurnCount { get; set; }

    public string? Intent { get; set; }
    public string? Phase { get; set; }
    public string? CustomerId { get; set; }

    public string Sentiment { get; set; } = "neutral";
    public int ConsecutiveNegativeTurns { get; set; }
    public List<SentimentTimelineEntry> SentimentTimeline { get; set; } = new();

    public SessionRatingInfo? Rating { get; set; }
    public List<ApprovalSummary> Approvals { get; set; } = new();
    public List<EscalationSummary> Escalations { get; set; } = new();

    public Dictionary<string, string> CollectedInfo { get; set; } = new();
}

public sealed record SentimentTimelineEntry(int Turn, string Label, double Score, DateTime At);
public sealed record SessionRatingInfo(int Stars, string? Feedback, DateTime RatedAt);
public sealed record ApprovalSummary(string Id, string Status, string ToolName, DateTime CreatedAt);
public sealed record EscalationSummary(string Id, string Status, string Reason, DateTime CreatedAt);
```

`TracePortService.GetSessionsSummary` bu nesneleri üretir — 4 farklı port'tan (session, rating, approval, escalation) veriyi birleştirir.

---

## AnalyticsDashboard

Global metrikler — `IRatingStore`, `ISessionManager`, `IApprovalQueue`, `IEscalationSink`'ten aggregate edilir:

```csharp
public sealed class AnalyticsDashboard
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public double AverageRating { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new(); // 1-5 stars

    public Dictionary<string, int> IntentDistribution { get; set; } = new();
    public Dictionary<string, int> SentimentDistribution { get; set; } = new();

    public int OpenEscalations { get; set; }
    public int PendingApprovals { get; set; }
    public int ActiveHumanAgents { get; set; }
}
```

`AnalyticsPortService.GetDashboard` döner. Admin paneli ana sayfa.

---

## ConversationRating

Konuşma sonunda kullanıcının yıldız + feedback verdiği kayıt.

```csharp
public sealed class ConversationRating
{
    public string SessionId { get; init; }
    public int Stars { get; init; }                      // 1-5
    public string? Feedback { get; init; }
    public DateTime RatedAt { get; init; }
}
```

`IRatingStore` üzerinden saklanır.

**Validasyon:** `Stars` 1-5 aralığında olmalı (`AnalyticsPortService.SubmitRating` kontrol eder).

### LessonMiner kullanımı

Düşük rating (≤2) kayıtları `LessonMiner` için **candidate trigger**:
- Düşük rating → ilgili trace'i al → LLM analiz → Lesson öner

---

## SlaEvent

SLA Guardian (`SlaPortService`) tarafından üretilen event'ler — geçikme veya breach tespiti.

```csharp
public sealed class SlaEvent
{
    public string Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string Kind { get; init; }                    // "approval" | "escalation"
    public string Severity { get; init; }                // "warn" | "breach"
    public string TargetId { get; init; }                // Approval/Escalation ID
    public long AgeSeconds { get; init; }
    public string? Action { get; init; }                 // "AutoReject", "AutoApprove", "PriorityBoost"
    public string? Note { get; init; }
}
```

### Akış

```
SLA Guardian her 30 saniyede tarar:
  - Pending approval > warnThreshold (örn. 60s) → warn event
  - Pending approval > breachThreshold (örn. 300s) → breach event + Action

Action:
  - AutoReject: Approval otomatik reddedilir
  - AutoApprove: Otomatik onaylanır (low-risk)
  - PriorityBoost: Escalation priority Critical'a çıkar
```

### LastEmittedAt (duplicate önleme)

`ISlaEventSink.LastEmittedAt(kind, targetId, severity)` aynı (kind, targetId, severity) için son emit zamanını döner. SLA Guardian bunu kontrol eder; aynı approval için her 30 saniyede `warn` event yazmaz — sadece **bir kez** yazar.

---

## Bağlantılar

- [Application TracePortService](../application/TracePortService.md)
- [Application AnalyticsPortService](../application/AnalyticsPortService.md)
- [Application SlaGuardian](../application/SlaGuardian.md)
- [Persistence ReasoningTrace adapter](../adapters-persistence/PostgresAdapters.md#postgresreasoningtracestore)
