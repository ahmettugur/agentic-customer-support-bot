// `observability.workflow_checkpoints` — MAF'ın serileştirdiği workflow durumu (adım başına bir satır).
// Yük `text` olarak, bayt bayt aynen saklanır (jsonb anahtar sırasını bozar; bkz. WorkflowCheckpointConfiguration).

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;

public sealed class WorkflowCheckpointEntity
{
    public string TraceId { get; set; } = "";
    public string CheckpointId { get; set; } = "";
    public string? ParentCheckpointId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
