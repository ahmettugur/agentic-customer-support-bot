using CustomerSupportBot.Domain.Model.Workflow;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Workflow tanım CRUD ve test koşturma için primary (driving) port.
/// </summary>
public interface IWorkflowPort
{
    IReadOnlyList<WorkflowDefinition> GetAll();
    WorkflowDefinition? Get(string id);
    WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null);
    bool Delete(string id);
    (WorkflowExecutionResult? Result, string? Error) Test(string id, string input, Dictionary<string, string>? variables = null);
}
