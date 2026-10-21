// `observability.workflow_turns` — checkpoint'lenen bir workflow turunun yeniden başlatılması için gereken bağlam.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;

public sealed class WorkflowTurnEntity
{
    public string TraceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Query { get; set; } = "";
    public string? CustomerId { get; set; }
    public string? ReasoningJson { get; set; }
    public int HistoryCount { get; set; }
    public DateTime StartedAt { get; set; }
}
