// Ports/Driven/Persistence/IWorkflowDefinitionRepository.cs
// SECONDARY PORT — Low-code workflow tanım kalıcılığı.

using CustomerSupportBot.Domain.Model.Workflow;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Persisted (in-memory veya Postgres) workflow definition secondary port.
/// Adaptörler: PostgresWorkflowDefinitionStore, InMemoryWorkflowDefinitionStore.
/// </summary>
public interface IWorkflowDefinitionRepository
{
    IReadOnlyList<WorkflowDefinition> GetAll();
    IReadOnlyList<WorkflowDefinition> GetActive();
    WorkflowDefinition? Get(string id);

    /// <summary>Insert veya replace — Id verilmemişse slug'a çevrilir.</summary>
    WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null);

    bool Delete(string id);
}
