// Infrastructure/Persistence/Entities/Workflow/WorkflowDefinitionEntity.cs
// `workflow.workflow_definitions` tablosu.
// TriggerKeywords, InputPatterns ve Steps listeleri JSONB olarak saklanır.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Workflow;

public sealed class WorkflowDefinitionEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string TriggerKeywordsJson { get; set; } = "[]";
    public string InputPatternsJson { get; set; } = "{}";
    public string StepsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
