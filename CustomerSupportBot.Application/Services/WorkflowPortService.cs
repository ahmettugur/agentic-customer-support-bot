// Application/Services/WorkflowPortService.cs
// DRIVING PORT IMPL — IWorkflowPort → IWorkflowDefinitionStore + WorkflowExecutor.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services.Workflow;
using CustomerSupportBot.Domain.Model.Workflow;

namespace CustomerSupportBot.Application.Services;

public sealed class WorkflowPortService : IWorkflowPort
{
    private readonly IWorkflowDefinitionStore _store;
    private readonly WorkflowExecutor _executor;

    public WorkflowPortService(IWorkflowDefinitionStore store, WorkflowExecutor executor)
    {
        _store = store;
        _executor = executor;
    }

    public IReadOnlyList<WorkflowDefinition> GetAll() => _store.GetAll();

    public WorkflowDefinition? Get(string id) => _store.Get(id);

    public WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null)
        => _store.Upsert(definition, updatedBy);

    public bool Delete(string id) => _store.Delete(id);

    public (WorkflowExecutionResult? Result, string? Error) Test(string id, string input, Dictionary<string, string>? variables = null)
    {
        var def = _store.Get(id);
        if (def is null)
            return (null, "Workflow not found.");

        var result = _executor.Execute(def, input, variables);
        return (result, null);
    }
}
