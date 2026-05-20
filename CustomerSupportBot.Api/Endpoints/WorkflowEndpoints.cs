// Endpoints/WorkflowEndpoints.cs
// Low-Code Workflow Designer — Admin CRUD + test-run endpoint.

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Driving;
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

        // ─── Create / Update (upsert) ───
        group.MapPost("", (WorkflowDefinition def, IWorkflowPort port, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(def.Name))
                return Results.BadRequest(new { error = "name is required." });

            var saved = port.Upsert(def, user.Identity?.Name);
            return Results.Created($"/workflows/{saved.Id}", saved);
        });

        group.MapPut("/{id}", (string id, WorkflowDefinition def, IWorkflowPort port, ClaimsPrincipal user) =>
        {
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
}
