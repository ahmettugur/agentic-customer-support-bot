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
    public long EstimatedTokens { get; set; }
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
public class SessionAnalytics
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }

    public int MessageCount { get; set; }
    public int TurnCount { get; set; }

    public string? CurrentIntent { get; set; }
    public string? Phase { get; set; }
    public string? CustomerId { get; set; }

    public string? Sentiment { get; set; }
    public double SentimentScore { get; set; }
    public int ConsecutiveNegativeTurns { get; set; }
    public List<SentimentTimelineEntry> SentimentTimeline { get; set; } = new();

    public SessionRatingInfo? Rating { get; set; }

    public int TotalApprovals { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public List<ApprovalSummary> ApprovalDetails { get; set; } = new();

    public int TotalEscalations { get; set; }
    public int OpenEscalations { get; set; }
    public int ResolvedEscalations { get; set; }
    public List<EscalationSummary> EscalationDetails { get; set; } = new();

    public Dictionary<string, string> CollectedInfo { get; set; } = new();
}

public class SentimentTimelineEntry
{
    public int Turn { get; set; }
    public string Label { get; set; } = "";
    public double Score { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SessionRatingInfo
{
    public int Stars { get; set; }
    public string? Feedback { get; set; }
    public DateTime RatedAt { get; set; }
}

public class ApprovalSummary
{
    public string Id { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}

public class EscalationSummary
{
    public string Id { get; set; } = "";
    public string? AgentName { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Resolution { get; set; }
}
```

`TracePortService.GetSessionsSummary` bu nesneleri üretir — 4 farklı port'tan (session, rating, approval, escalation) veriyi birleştirir.

---

## AnalyticsDashboard

Global metrikler — `IRatingStore`, `ISessionManager`, `IApprovalQueue`, `IEscalationSink`'ten aggregate edilir:

```csharp
public class AnalyticsDashboard
{
    // Genel
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public double AverageSessionMessages { get; set; }

    // Rating
    public double AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
    public List<ConversationRating> RecentRatings { get; set; } = new();

    // Approvals
    public int TotalApprovals { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int PendingCount { get; set; }

    // Escalations
    public int TotalEscalations { get; set; }
    public int OpenEscalations { get; set; }
    public int AcknowledgedEscalations { get; set; }
    public int ResolvedEscalations { get; set; }
    public int DismissedEscalations { get; set; }

    // Intent & Faz dağılımı
    public Dictionary<string, int> IntentDistribution { get; set; } = new();
    public Dictionary<string, int> PhaseDistribution { get; set; } = new();

    // Duygu analizi
    public double AverageSentimentScore { get; set; }
    public Dictionary<string, int> SentimentDistribution { get; set; } = new();
    public int NegativeSessionCount { get; set; }
    public int SentimentAlertCount { get; set; }
}
```

`AnalyticsPortService.GetDashboard` döner. Admin paneli ana sayfa.

---

## ConversationRating

Konuşma sonunda kullanıcının yıldız + feedback verdiği kayıt.

```csharp
public class ConversationRating
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SessionId { get; set; } = "";
    public int Stars { get; set; }                       // 1-5
    public string? Feedback { get; set; }
    public DateTime RatedAt { get; set; } = DateTime.UtcNow;
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
    public int AgeSeconds { get; set; }
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
