// Services/Workflow/IWorkflowDefinitionStore.cs

using CustomerSupportBot.Api.Models.Workflow;

namespace CustomerSupportBot.Api.Services.Workflow;

/// <summary>Persisted (in-memory veya Postgres) workflow definition store.</summary>
public interface IWorkflowDefinitionStore
{
    IReadOnlyList<WorkflowDefinition> GetAll();
    IReadOnlyList<WorkflowDefinition> GetActive();
    WorkflowDefinition? Get(string id);

    /// <summary>Insert veya replace — Id verilmemişse slug'a çevrilir.</summary>
    WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null);

    bool Delete(string id);
}
