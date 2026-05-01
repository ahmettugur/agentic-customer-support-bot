// Infrastructure/Persistence/Entities/Hitl/EscalationEntity.cs
// `hitl.escalations` tablosu — needs_escalation status'lu reflection'lardan
// türetilen eskalasyon kayıtları. Status: Open|Acknowledged|Resolved|Dismissed.

namespace CustomerSupportBot.Infrastructure.Persistence.Entities.Hitl;

public sealed class EscalationEntity
{
    public string Id { get; set; } = "";
    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string? AgentName { get; set; }
    public string UserQuery { get; set; } = "";
    public string Reason { get; set; } = "";

    /// <summary>List&lt;string&gt; missing_context JSONB.</summary>
    public string MissingContextJson { get; set; } = "[]";

    public string? ResponseSummary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Status { get; set; } = "Open";
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
}
