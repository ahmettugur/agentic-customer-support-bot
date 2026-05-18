// Infrastructure/Persistence/Entities/Hitl/ApprovalRequestEntity.cs
// `hitl.approval_requests` tablosu — HITL onay kayıtları.
// Status: ApprovalStatus enum'unun string karşılığı (Pending|Approved|Rejected|Expired).
// TaskCompletionSource persist edilmez — in-memory kalır.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;

public sealed class ApprovalRequestEntity
{
    public string Id { get; set; } = "";
    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string ToolName { get; set; } = "";
    public string? AgentName { get; set; }

    /// <summary>Tool parametreleri (Dictionary) JSONB.</summary>
    public string ParametersJson { get; set; } = "{}";

    public string? UserQuery { get; set; }
    public string? Justification { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecidedBy { get; set; }
    public string? DecisionReason { get; set; }
    public int TimeoutSeconds { get; set; }
}
