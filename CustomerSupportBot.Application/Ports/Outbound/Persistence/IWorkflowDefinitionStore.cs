using CustomerSupportBot.Domain.Model.Workflow;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Workflow definition kalıcılığı için secondary port.
///</summary>
public interface IWorkflowDefinitionStore
{
    IReadOnlyList<WorkflowDefinition> GetAll();
    IReadOnlyList<WorkflowDefinition> GetActive();
    WorkflowDefinition? Get(string id);

    /// <summary>Insert veya replace — Id verilmemişse slug'a çevrilir.</summary>
    WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null);

    bool Delete(string id);
}
