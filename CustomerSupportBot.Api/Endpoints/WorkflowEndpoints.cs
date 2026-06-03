// Endpoints/WorkflowEndpoints.cs
// Low-Code Workflow Designer — Admin CRUD + test-run endpoint.

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model.Workflow;

namespace CustomerSupportBot.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/workflows");

        group.MapGet("", (IWorkflowPort port) =>
        {
            var all = port.GetAll();
            return Results.Ok(new { count = all.Count, items = all });
        });

        group.MapGet("/{id}", (string id, IWorkflowPort port) =>
        {
            var d = port.Get(id);
            return d is null ? Results.NotFound() : Results.Ok(d);
        });

        group.MapPost("", (WorkflowRequest req, IWorkflowPort port, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "name is required." });

            var def   = MapToDefinition(req);
            var saved = port.Upsert(def, user.Identity?.Name);
            return Results.Created($"/workflows/{saved.Id}", saved);
        });

        group.MapPut("/{id}", (string id, WorkflowRequest req, IWorkflowPort port, ClaimsPrincipal user) =>
        {
            var def = MapToDefinition(req);
            def.Id  = id;
            var saved = port.Upsert(def, user.Identity?.Name);
            return Results.Ok(saved);
        });

        group.MapDelete("/{id}", (string id, IWorkflowPort port) =>
            port.Delete(id) ? Results.NoContent() : Results.NotFound());

        group.MapPost("/{id}/test", (string id, TestRunInput input, IWorkflowPort port) =>
        {
            var (result, error) = port.Test(id, input.Input ?? "", input.Variables);
            return error is not null
                ? Results.NotFound(new { error })
                : Results.Ok(result);
        });

        return app;
    }

    private static WorkflowDefinition MapToDefinition(WorkflowRequest req) => new()
    {
        Name            = req.Name,
        Description     = req.Description,
        Version         = req.Version,
        IsActive        = req.IsActive,
        StartStepId     = req.StartStepId,
        TriggerKeywords = req.TriggerKeywords,
        InputPatterns   = req.InputPatterns,
        Steps           = req.Steps.Select(s => new WorkflowStep
        {
            Id            = s.Id ?? Guid.NewGuid().ToString("N")[..6],
            Type          = Enum.TryParse<WorkflowStepType>(s.Type, true, out var t)
                                ? t : WorkflowStepType.Respond,
            Label         = s.Label,
            Next          = s.Next,
            OnTrue        = s.OnTrue,
            OnFalse       = s.OnFalse,
            Template      = s.Template,
            Tool          = s.Tool,
            Parameters    = s.Parameters,
            StoreAs       = s.StoreAs,
            Condition     = s.Condition,
            VariableName  = s.VariableName,
            VariableValue = s.VariableValue
        }).ToList()
    };
}
