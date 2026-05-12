// Endpoints/WorkflowEndpoints.cs
// Low-Code Workflow Designer — Admin CRUD + test-run endpoint.
// /workflows altında map edilir, RequireAuthorization("Admin") scope altında.

using System.Security.Claims;
using CustomerSupportBot.Api.Models.Workflow;
using CustomerSupportBot.Api.Services.Workflow;

namespace CustomerSupportBot.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/workflows");

        // ─── List ───
        group.MapGet("", (IWorkflowDefinitionStore store) =>
        {
            var all = store.GetAll();
            return Results.Ok(new { count = all.Count, items = all });
        });

        // ─── Get ───
        group.MapGet("/{id}", (string id, IWorkflowDefinitionStore store) =>
        {
            var d = store.Get(id);
            return d is null ? Results.NotFound() : Results.Ok(d);
        });

        // ─── Create / Update (upsert) ───
        group.MapPost("", (WorkflowDefinition def, IWorkflowDefinitionStore store, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(def.Name))
                return Results.BadRequest(new { error = "name is required." });

            var saved = store.Upsert(def, user.Identity?.Name);
            return Results.Created($"/workflows/{saved.Id}", saved);
        });

        group.MapPut("/{id}", (string id, WorkflowDefinition def, IWorkflowDefinitionStore store, ClaimsPrincipal user) =>
        {
            def.Id = id;
            var saved = store.Upsert(def, user.Identity?.Name);
            return Results.Ok(saved);
        });

        // ─── Delete ───
        group.MapDelete("/{id}", (string id, IWorkflowDefinitionStore store) =>
        {
            return store.Delete(id) ? Results.NoContent() : Results.NotFound();
        });

        // ─── Test run ───
        group.MapPost("/{id}/test", (string id, TestRunInput input,
            IWorkflowDefinitionStore store, WorkflowExecutor executor) =>
        {
            var def = store.Get(id);
            if (def is null) return Results.NotFound(new { error = "Workflow not found." });

            var result = executor.Execute(def, input.Input ?? "", input.Variables);
            return Results.Ok(result);
        });

        return app;
    }
}

public class TestRunInput
{
    public string? Input { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}
