using System.Text.Json;

namespace CustomerSupportBot.Web.Models;

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
    public JsonElement? Reasoning { get; init; }
    public JsonElement? Planning { get; init; }
    public List<TraceAgentVisit> AgentVisits { get; init; } = [];
    public List<JsonElement> SpecialistReasonings { get; init; } = [];
    public List<TraceToolCall> ToolCalls { get; init; } = [];
    public bool WasRevised { get; init; }
    public string? FirstDraftResponse { get; init; }
    public JsonElement? SelfCritique { get; init; }
}

public sealed record TraceAgentVisit(
    string? AgentName,
    DateTimeOffset StartedAt,
    int? DurationMs,
    string? Output
);

public sealed record TraceToolCall(
    string? ToolName,
    string? AgentName,
    DateTimeOffset? InvokedAt,
    bool Success,
    string? ParametersSummary,
    string? ResultSummary
);

// ─── Replay step model (built client-side from TraceDetail) ──────────────────

public abstract record ReplayStepPayload;
public sealed record ReplayInitPayload(string? TraceId, string? SessionId, string? UserQuery) : ReplayStepPayload;
public sealed record ReplayFinalPayload(string? TerminationReason, int? DurationMs, int? IterationCount, string? Error, string? Response) : ReplayStepPayload;
public sealed record ReplayToolPayload(string? ToolName, string? AgentName, bool Success, string? Parameters, string? Result) : ReplayStepPayload;
public sealed record ReplayAgentPayload(string? AgentName, int? DurationMs, string? Output) : ReplayStepPayload;
public sealed record ReplayJsonPayload(string Json) : ReplayStepPayload;

public sealed record ReplayStep(string Kind, DateTimeOffset? Time, string Title, ReplayStepPayload Payload);
