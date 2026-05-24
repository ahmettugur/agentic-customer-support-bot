# Web Models — DTO'lar

**Klasör:** `Models/`

API response'larını deserialize etmek için kullanılan client-side model'ler. Domain modellerine paralel ama **WASM JSON serialization** odaklı.

| Dosya | İçerik |
|---|---|
| `AdminModels.cs` | Approval, Escalation, ChatSession, Analytics, Agent, Sla, Lesson, ChatHistoryMessage |
| `TraceDetailModels.cs` | TraceSession, TraceDetail, ReasoningSummary, PlanningSummary, AgentVisit, ToolInvocation |
| `WorkflowModels.cs` | WorkflowSummary, WorkflowListResponse, WorkflowExecutionResult, WorkflowStepTrace |

---

## Domain modeli kopyalamak yerine ortak proje?

Bu projede Web kendi DTO'larını yazıyor — `Domain` projesini referans **etmiyor**. Avantajlar:

- **Decoupling**: Server-side Domain değişse de Web ayrı build edilebilir
- **Field naming**: JsonSerializer naming convention (camelCase) ile uyumlu
- **Optional fields**: Web hangi alanları kullanacaksa onları tanımlar, fazlasını ignore eder

Dezavantaj: Duplicate kod (her iki tarafta `EscalationRequest` tanımı var).

Alternatif (gelecek): Paylaşılan `Shared.Contracts` projesi — DTO'lar burada.

---

## AdminModels.cs

### ApprovalRequest

```csharp
public sealed class ApprovalRequest
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string? TraceId { get; set; }
    public string ToolName { get; set; } = "";
    public string AgentName { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public string UserQuery { get; set; } = "";
    public string? Justification { get; set; }
    public string Status { get; set; } = "Pending";   // Pending | Approved | Rejected | Expired
    public DateTime CreatedAt { get; set; }
    public int TimeoutSeconds { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionReason { get; set; }
}
```

`Status` enum yerine `string` — server JSON enum convention'ı (camelCase) kullanır:

```json
{ "status": "Pending" }   // veya "pending" — JsonStringEnumConverter ile uyumlu
```

### EscalationRequest

```csharp
public sealed class EscalationRequest
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserQuery { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> MissingContext { get; set; } = new();
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
    public List<string> RequiredSkills { get; set; } = new();
    public string Priority { get; set; } = "Normal";
    public string? SuggestedAgentId { get; set; }
    public string? SuggestedAgentName { get; set; }
    public double MatchScore { get; set; }
    public string? RoutingNote { get; set; }
}
```

### ActiveChatSession

```csharp
public sealed class ActiveChatSession
{
    public string SessionId { get; set; } = "";
    public string HumanAgent { get; set; } = "";
    public DateTime EnteredAt { get; set; }
    public int MessageCount { get; set; }
}
```

Admin paneli "Active Chats" tab'ı bu liste'yi gösterir.

### ChatHistoryMessage

```csharp
public sealed class ChatHistoryMessage
{
    public string Id { get; set; } = "";
    public string Sender { get; set; } = "";   // user | bot | admin | system | botTyping
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
```

### AnalyticsDashboard

```csharp
public sealed class AnalyticsDashboard
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public double AverageRating { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();   // 1-5
    public Dictionary<string, int> IntentDistribution { get; set; } = new();
    public Dictionary<string, int> SentimentDistribution { get; set; } = new();
    public Dictionary<string, int> PhaseDistribution { get; set; } = new();
    public double AverageMessagesPerSession { get; set; }
    public double AverageSentimentScore { get; set; }
    public int NegativeSessionsCount { get; set; }
    public int SentimentAlertsCount { get; set; }
    public int OpenEscalations { get; set; }
    public int PendingApprovals { get; set; }
    public int ActiveHumanAgents { get; set; }
}
```

Dashboard bar chart'larını besler.

### AgentInfo

```csharp
public sealed class AgentInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public List<string> Skills { get; set; } = new();
    public List<string> Languages { get; set; } = new();
    public int MaxConcurrentLoad { get; set; }
    public int CurrentLoad { get; set; }
    public int Priority { get; set; }
}
```

Escalation assign modal'da dropdown'a beslenir.

### SlaStatus, SlaEvent

```csharp
public sealed class SlaStatus
{
    public SlaQueueStatus Approvals { get; set; } = new();
    public SlaQueueStatus Escalations { get; set; } = new();
}

public sealed class SlaQueueStatus
{
    public int PendingCount { get; set; }
    public long OldestAgeSeconds { get; set; }
    public long WarnThresholdSeconds { get; set; }
    public long BreachThresholdSeconds { get; set; }
    public int BreachCount { get; set; }
    public string OnBreachAction { get; set; } = "";
}

public sealed class SlaEvent
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Kind { get; set; } = "";        // "approval" | "escalation"
    public string Severity { get; set; } = "";    // "warn" | "breach"
    public string TargetId { get; set; } = "";
    public long AgeSeconds { get; set; }
    public string? Action { get; set; }
    public string? Note { get; set; }
}
```

### LessonProposal

```csharp
public sealed class LessonProposal
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string LessonText { get; set; } = "";
    public string Observation { get; set; } = "";
    public string? SuggestedAgent { get; set; }
    public List<string> SourceTraceIds { get; set; } = new();
    public string Status { get; set; } = "Proposed";
    public DateTime CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string? VectorMemoryId { get; set; }
}
```

Admin "Improvements" tab'ı bu modeli render eder.

---

## TraceDetailModels.cs

### TraceSession (sidebar listesi)

```csharp
public sealed class TraceSession
{
    public string SessionId { get; set; } = "";
    public string? Title { get; set; }                   // İlk user mesajı
    public int TraceCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime LastTraceAt { get; set; }
}
```

### TraceDetail (tek trace tam içerik)

```csharp
public sealed class TraceDetail
{
    public string TraceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserQuery { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int EstimatedTokens { get; set; }
    public string? Error { get; set; }
    public string? TerminationReason { get; set; }
    public string? FinalResponse { get; set; }
    public int IterationCount { get; set; }

    public ReasoningSummary? Reasoning { get; set; }
    public PlanningSummary? Planning { get; set; }
    public List<AgentVisit> AgentVisits { get; set; } = new();
    public List<ToolInvocation> ToolCalls { get; set; } = new();
}
```

### ReasoningSummary

```csharp
public sealed class ReasoningSummary
{
    public string Analysis { get; set; } = "";
    public string? Intent { get; set; }
    public string? Confidence { get; set; }      // string ("yüksek") veya numeric
    public double ConfidenceScore { get; set; }
    public List<string>? RequiredInfo { get; set; }
    public List<ReasoningStepSummary>? Steps { get; set; }
    public string? Sentiment { get; set; }
    public double SentimentScore { get; set; }
}

public sealed class ReasoningStepSummary
{
    public int Order { get; set; }
    public string Description { get; set; } = "";
    public string? Action { get; set; }
    public string? Grounding { get; set; }
    public double Confidence { get; set; }
}
```

### PlanningSummary

```csharp
public sealed class PlanningSummary
{
    public string DetectedIntent { get; set; } = "";
    public double IntentConfidence { get; set; }
    public string SelectedAgent { get; set; } = "";
    public string? Rationale { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
}
```

### AgentVisit, ToolInvocation

```csharp
public sealed class AgentVisit
{
    public string AgentName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Output { get; set; }
}

public sealed class ToolInvocation
{
    public string ToolName { get; set; } = "";
    public DateTime InvokedAt { get; set; }
    public string AgentName { get; set; } = "";
    public string? ParametersSummary { get; set; }
    public string? ResultSummary { get; set; }
    public bool Success { get; set; }
}
```

---

## WorkflowModels.cs

### WorkflowListResponse

```csharp
public sealed class WorkflowListResponse
{
    public int Count { get; set; }
    public List<WorkflowSummary> Items { get; set; } = new();
}

public sealed class WorkflowSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public int StepCount { get; set; }
}
```

### WorkflowExecutionResult

```csharp
public sealed class WorkflowExecutionResult
{
    public string WorkflowId { get; set; } = "";
    public bool Success { get; set; }
    public string? FinalResponse { get; set; }
    public List<WorkflowStepTrace> StepTraces { get; set; } = new();
    public Dictionary<string, string> FinalVariables { get; set; } = new();
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public sealed class WorkflowStepTrace
{
    public string StepId { get; set; } = "";
    public string Type { get; set; } = "";    // Respond | Lookup | Branch | SetVariable
    public string? Label { get; set; }
    public bool Skipped { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}
```

WorkflowDesigner test result paneli bu yapıyı pretty-print eder.

---

## JSON serialization

Tüm DTO'lar System.Text.Json ile deserialize edilir:

```csharp
var resp = await _http.GetFromJsonAsync<TraceDetail>(url);
```

Naming convention: server camelCase yayar, C# PascalCase property — default mapping çalışır.

Enum'lar string olarak gelir (`status: "Pending"`) — eğer C# tarafında `string` field'ı kullanıyorsanız direkt çalışır. Enum kullanıyorsanız `JsonStringEnumConverter` gerekir.

---

## Bağlantılar

- [Domain Model katmanı](../domain/README.md) — server tarafı karşılıkları
- [Services.md](Services.md) — bu modelleri kullanan service'ler
- [Pages-Admin.md](Pages-Admin.md) — modellerin UI rendering'i
