// Endpoints/AgentsEndpoints.cs
// Smart Routing & Skills-Based Escalation — Admin endpoints for human agent registry.

using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents");

        // ─── List (registry + auth-linked users merged) ───
        group.MapGet("", async (IHumanAgentPort port, CancellationToken ct) =>
        {
            var merged = await port.GetAllMergedAsync(ct);
            var items = merged.Select(a => new { id = a.Id, displayName = a.DisplayName, isActive = a.IsActive });
            return Results.Ok(new { count = merged.Count, items });
        });

        // ─── Create ───
        group.MapPost("", (HumanAgentInput input, IHumanAgentPort port) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "displayName is required." });

            var agent = new HumanAgent
            {
                DisplayName = input.DisplayName.Trim(),
                Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
                Skills = input.Skills ?? new(),
                Languages = input.Languages ?? new(),
                IsActive = input.IsActive ?? true,
                MaxConcurrentLoad = input.MaxConcurrentLoad is > 0 ? input.MaxConcurrentLoad.Value : 5,
                Priority = input.Priority ?? 0
            };
            var created = port.CreateAgent(agent);
            return Results.Created($"/agents/{created.Id}", created);
        });

        // ─── Get one ───
        group.MapGet("/{id}", (string id, IHumanAgentPort port) =>
        {
            var a = port.GetAgent(id);
            return a is null ? Results.NotFound() : Results.Ok(a);
        });

        // ─── Update ───
        group.MapPut("/{id}", (string id, HumanAgentInput input, IHumanAgentPort port) =>
        {
            var updated = port.UpdateAgent(id, input);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        // ─── Delete ───
        group.MapDelete("/{id}", (string id, IHumanAgentPort port) =>
        {
            var ok = port.DeleteAgent(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // ─── Reroute escalation (manual override) ───
        app.MapPost("/escalations/{id}/reroute", (string id, RerouteInput input, IHumanAgentPort port) =>
        {
            var result = port.RerouteEscalation(id, input.AgentId, input.Reason);
            if (result.Error != null)
                return result.Error.Contains("Escalation")
                    ? Results.NotFound(new { error = result.Error })
                    : Results.BadRequest(new { error = result.Error });

            return Results.Ok(result.Updated);
        });

        return app;
    }
}
