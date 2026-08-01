# Web Models — DTO'lar

**Klasör:** `Models/`

API response'larını deserialize etmek için kullanılan client-side model'ler. Domain modellerine paralel ama **WASM JSON serialization** odaklı.

| Dosya | İçerik |
|---|---|
| `AdminModels.cs` | Approval, Escalation, ChatSession, Analytics, Agent, SLA, Lesson, SessionAnalytics |
| `TraceDetailModels.cs` | TraceDetail, TraceAgentVisit, TraceToolCall, ReplayStep |

---

## Domain modeli kopyalamak yerine ortak proje?

Bu projede Web kendi DTO'larını yazıyor — `Domain` projesini referans **etmiyor**. Avantajlar:

- **Decoupling**: Server-side Domain değişse de Web ayrı build edilebilir
- **Field naming**: JsonSerializer camelCase ile uyumlu
- **Optional fields**: Web hangi alanları kullanacaksa tanımlar, fazlasını ignore eder

Dezavantaj: Duplicate kod. Alternatif (gelecek): Paylaşılan `Shared.Contracts` projesi.

---

## AdminModels.cs

### ApprovalRequest

```csharp
public sealed record ApprovalRequest(
    string Id,
    string ToolName,
    string? AgentName,
    string? UserQuery,
    string? SessionId,
    object? Parameters,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? DecisionReason
);
```

`Status` string: `"Pending"` | `"Approved"` | `"Rejected"` | `"TimedOut"`.

### EscalationRequest

```csharp
public sealed record EscalationRequest(
    string Id,
    string? AgentName,
    string? AssignedTo,
    string? Reason,
    string? UserQuery,
    string? SessionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution,
    string[]? MissingContext,
    string? ResponseSummary
);
```

### ActiveChatSession

```csharp
public sealed record ActiveChatSession(
    string SessionId,
    string? HumanAgent,
    int MessageCount,
    string? SentimentLabel,
    double? SentimentScore,
    DateTimeOffset EnteredHumanModeAt
);
```

Admin paneli "Active Chats" tab'ı bu liste'yi gösterir.

### ChatHistoryMessage

```csharp
public sealed record ChatHistoryMessage(
    string Sender,
    string Text,
    DateTimeOffset Timestamp
);
```

`Sender`: `"user"` | `"bot"` | `"admin"` | `"system"`.

### AnalyticsDashboard

```csharp
public sealed record AnalyticsDashboard(
    int TotalSessions,
    int TotalMessages,
    double AverageRating,
    int TotalRatings,
    double AverageMessagesPerSession,
    double AverageSentimentScore,
    int NegativeSessions,
    int SentimentAlerts,
    Dictionary<string, int>? RatingDistribution,
    Dictionary<string, int>? SentimentDistribution,
    Dictionary<string, int>? IntentDistribution,
    Dictionary<string, int>? PhaseDistribution,
    ApprovalStats? ApprovalStats,
    EscalationStats? EscalationStats,
    RecentRating[]? RecentRatings
);

public sealed record ApprovalStats(int Total, int Approved, int Rejected, int TimedOut);
public sealed record EscalationStats(int Total, int Resolved, int Dismissed);
public sealed record RecentRating(string SessionId, int Stars, string? Feedback, DateTimeOffset RatedAt);
```

Dashboard tab'ının tüm verisi tek API çağrısından gelir.

### SessionSummary

```csharp
public sealed record SessionSummary(
    string SessionId, DateTimeOffset LastActivity, int MessageCount, string? Title = null);
```

Admin paneli session listesi için.

### SessionAnalyticsModel

```csharp
public sealed record SessionAnalyticsModel(
    string SessionId,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    int TurnCount,
    string? CurrentIntent,
    string? Phase,
    string? CustomerId,
    string? Sentiment,
    double SentimentScore,
    int ConsecutiveNegativeTurns,
    List<SentimentTimelineItem> SentimentTimeline,
    SessionRatingItem? Rating,
    int TotalApprovals,
    int ApprovedCount,
    int RejectedCount,
    int ExpiredCount,
    List<ApprovalSummaryItem> ApprovalDetails,
    int TotalEscalations,
    int OpenEscalations,
    int ResolvedEscalations,
    List<EscalationSummaryItem> EscalationDetails,
    Dictionary<string, string> CollectedInfo
);

public sealed record SentimentTimelineItem(int Turn, string Label, double Score, DateTime Timestamp);
public sealed record SessionRatingItem(int Stars, string? Feedback, DateTime RatedAt);
public sealed record ApprovalSummaryItem(string Id, string ToolName, string Status, DateTime RequestedAt, DateTime? DecidedAt, string? DecidedBy);
public sealed record EscalationSummaryItem(string Id, string? AgentName, string Reason, string Status, DateTime CreatedAt, string? Resolution);
```

`GET /analytics/session/{sid}` response modeli. Tek session'ın tüm analytics detayı.

### AgentInfo

```csharp
public sealed record AgentInfo(string Id, string DisplayName, bool IsActive);
```

`GET /agents` merge listesinden dönüyor. Escalation assign modalı için.

### SLA modelleri

```csharp
public sealed record SlaStatus(
    bool Enabled,
    int PollIntervalSeconds,
    SlaApprovalStats? Approvals,
    SlaEscalationStats? Escalations
);

public sealed record SlaApprovalStats(
    int PendingCount,
    double? OldestSeconds,
    double WarnAfter,
    double BreachAfter,
    string? OnBreach,
    int BreachCountRecent
);

public sealed record SlaEscalationStats(
    int OpenCount,
    double? OldestSeconds,
    double WarnAfter,
    double BreachAfter,
    bool BoostPriorityOnBreach,
    int BreachCountRecent
);

public sealed record SlaEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string Severity,
    string TargetId,
    string? Action,
    double? AgeSeconds,
    string? Note
);

public sealed record SlaEventsResponse(int TotalCount, List<SlaEvent> Items);
```

`SlaApiService` bu modelleri kullanır. `SlaEventsResponse.Items` `SlaApiService.GetEventsAsync` tarafından açılır.

### LessonProposal

```csharp
public sealed record LessonProposal(
    string Id,
    string? Title,
    string? LessonText,
    string? Observation,
    string? SuggestedAgent,
    string[]? SourceTraceIds,
    string Status,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionReason
);
```

Admin "Improvements" tab'ı bu modeli render eder.

---

## TraceDetailModels.cs

### TraceDetail

```csharp
public sealed class TraceDetail
{
    public string? TraceId { get; init; }
    public string? SessionId { get; init; }
    public string? UserQuery { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? TerminationReason { get; init; }
    public int? DurationMs { get; init; }
    public int? IterationCount { get; init; }
    public string? Error { get; init; }
    public string? FinalResponse { get; init; }
    public JsonElement? Reasoning { get; init; }      // raw JSON
    public JsonElement? Planning { get; init; }       // raw JSON
    public List<TraceAgentVisit> AgentVisits { get; init; } = [];
    public List<JsonElement> SpecialistReasonings { get; init; } = [];
    public List<TraceToolCall> ToolCalls { get; init; } = [];
    public bool WasRevised { get; init; }
    public string? FirstDraftResponse { get; init; }
    public JsonElement? FinalCritique { get; init; }
}
```

`Reasoning` ve `Planning` `JsonElement?` — server formatı değişebileceğinden raw JSON olarak tutulur, sayfa kendisi parse eder.

### TraceAgentVisit

```csharp
public sealed record TraceAgentVisit(
    string? AgentName,
    DateTimeOffset StartedAt,
    int? DurationMs,
    string? Output
);
```

### TraceToolCall

```csharp
public sealed record TraceToolCall(
    string? ToolName,
    string? AgentName,
    DateTimeOffset? InvokedAt,
    bool Success,
    string? ParametersSummary,
    string? ResultSummary
);
```

### Replay modelleri

Trace replay step'leri için client-side modeller — `TraceDetail`'den build edilir:

```csharp
public abstract record ReplayStepPayload;
public sealed record ReplayInitPayload(string? TraceId, string? SessionId, string? UserQuery) : ReplayStepPayload;
public sealed record ReplayFinalPayload(string? TerminationReason, int? DurationMs, int? IterationCount, string? Error, string? Response) : ReplayStepPayload;
public sealed record ReplayToolPayload(string? ToolName, string? AgentName, bool Success, string? Parameters, string? Result) : ReplayStepPayload;
public sealed record ReplayAgentPayload(string? AgentName, int? DurationMs, string? Output) : ReplayStepPayload;
public sealed record ReplayJsonPayload(string Json) : ReplayStepPayload;

public sealed record ReplayStep(string Kind, DateTimeOffset? Time, string Title, ReplayStepPayload Payload);
```

`ReplayStep.Kind`: `"init"` | `"final"` | `"tool"` | `"agent"` | `"json"`. Trace viewer bu adımları sırayla gösterir.

---

## JSON serialization

Tüm DTO'lar `System.Net.Http.Json` ile deserialize edilir:

```csharp
var resp = await http.GetFromJsonAsync<TraceDetail>(url);
```

Naming convention: server camelCase yayar, C# PascalCase property — default mapping çalışır (`record` positional parametreler camelCase ile eşleşir).

`record` tiplerde `JsonPropertyName` attribute gerekmez — positional constructor parametrelerini server JSON'unun field adlarıyla eşleştirmek için isimlendirme kuralına uyulur.

### JsonExtensions.cs

**Dosya:** `Helpers/JsonExtensions.cs`
**Erişim:** `internal static`

`GetFromJsonAsync<T>`'in tip-güvenli deserialization'ının yeterli olmadığı, ham `JsonElement` üzerinde çalışmayı gerektiren durumlar için (ör. şekli önceden tam bilinmeyen/opsiyonel alanlı SSE payload'ları) küçük bir `JsonElement` extension seti sağlar:

```csharp
internal static class JsonExtensions
{
    public static string? TryGetProp(this JsonElement el, string key)
    public static bool? TryGetBool(this JsonElement el, string key)
    public static int? TryGetInt(this JsonElement el, string key)
    public static List<string>? TryGetStringArray(this JsonElement el, string key)
}
```

Her metot `TryGetProperty` ile alanın var olup olmadığını kontrol eder — eksik/yanlış tipli alan exception fırlatmaz, `null` döner.

---

## Bağlantılar

- [Domain Model katmanı](../domain/README.md) — server tarafı karşılıkları
- [Services.md](Services.md) — bu modelleri kullanan service'ler
- [Pages-Admin.md](Pages-Admin.md) — modellerin UI rendering'i
