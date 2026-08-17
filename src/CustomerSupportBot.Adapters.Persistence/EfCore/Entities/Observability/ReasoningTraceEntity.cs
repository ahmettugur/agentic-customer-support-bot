// Infrastructure/Persistence/Entities/Observability/ReasoningTraceEntity.cs
// `observability.reasoning_traces` tablosu — Workflow trace'leri.
// İç içe nesneler (ReasoningResult, PlanningResult, SpecialistReasoning[],
// AgentVisit[], ToolInvocation[], ResponseCritique) JSONB string olarak
// saklanır. Update hot-path: in-memory mutate, DB sadece Complete()'te yazılır.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;

public sealed class ReasoningTraceEntity
{
    public string TraceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserQuery { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TerminationReason { get; set; }
    public string? FinalResponse { get; set; }
    public int IterationCount { get; set; }
    public string? Error { get; set; }
    public long EstimatedTokens { get; set; }
    public string? FirstDraftResponse { get; set; }
    public bool WasRevised { get; set; }

    public string? ReasoningJson { get; set; }
    public string? PlanningJson { get; set; }
    public string SpecialistReasoningsJson { get; set; } = "[]";
    public string? FinalCritiqueJson { get; set; }
    public string AgentVisitsJson { get; set; } = "[]";
    public string ToolCallsJson { get; set; } = "[]";
}
