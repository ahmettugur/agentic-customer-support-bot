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

        // ─── List ───
        group.MapGet("", (IWorkflowPort port) =>
        {
            var all = port.GetAll();
            return Results.Ok(new { count = all.Count, items = all });
        });

        // ─── Get ───
        group.MapGet("/{id}", (string id, IWorkflowPort port) =>
        {
            var d = port.Get(id);
            return d is null ? Results.NotFound() : Results.Ok(d);
        });

        // ─── Create ───
        group.MapPost("", (WorkflowRequest req, IWorkflowPort port, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "name is required." });

            var def = MapToDefinition(req);
            var saved = port.Upsert(def, user.Identity?.Name);
            return Results.Created($"/workflows/{saved.Id}", saved);
        });

        // ─── Update ───
        group.MapPut("/{id}", (string id, WorkflowRequest req, IWorkflowPort port, ClaimsPrincipal user) =>
        {
            var def = MapToDefinition(req);
            def.Id = id;
            var saved = port.Upsert(def, user.Identity?.Name);
            return Results.Ok(saved);
        });

        // ─── Delete ───
        group.MapDelete("/{id}", (string id, IWorkflowPort port) =>
        {
            return port.Delete(id) ? Results.NoContent() : Results.NotFound();
        });

        // ─── Test run ───
        group.MapPost("/{id}/test", (string id, TestRunInput input, IWorkflowPort port) =>
        {
            var (result, error) = port.Test(id, input.Input ?? "", input.Variables);
            return error != null
                ? Results.NotFound(new { error })
                : Results.Ok(result);
        });

        return app;
    }

    /// <summary>
    /// WorkflowRequest DTO → WorkflowDefinition domain modeli dönüşümü.
    /// Domain modeli doğrudan HTTP sınırına maruz kalmaz.
    /// </summary>
    private static WorkflowDefinition MapToDefinition(WorkflowRequest req) => new()
    {
        Name = req.Name,
        Description = req.Description,
        Version = req.Version,
        IsActive = req.IsActive,
        TriggerKeywords = req.TriggerKeywords,
        InputPatterns = req.InputPatterns,
        Steps = req.Steps.Select(s => new WorkflowStep
        {
            Id = s.Id ?? Guid.NewGuid().ToString("N")[..6],
            Type = Enum.TryParse<WorkflowStepType>(s.Type, true, out var t) ? t : WorkflowStepType.Respond,
            Label = s.Label,
            Template = s.Template,
            Tool = s.Tool,
            Parameters = s.Parameters,
            StoreAs = s.StoreAs,
            Condition = s.Condition,
            SkipNext = s.SkipNext,
            VariableName = s.VariableName,
            VariableValue = s.VariableValue
        }).ToList()
    };
}
