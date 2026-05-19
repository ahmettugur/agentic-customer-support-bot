// Endpoints/AgentsEndpoints.cs
// Smart Routing & Skills-Based Escalation — Admin endpoints for human agent registry.
// /agents (GET, POST, PUT/{id}, DELETE/{id}) ve /escalations/{id}/reroute.
// Hepsi RequireAuthorization("Admin") scope altında map edilir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents");

        // ─── List ───
        group.MapGet("", async (IHumanAgentRegistry registry, CancellationToken ct) =>
        {
            var all = registry.GetAll();

            // Auth tablosundaki Agent rolü + LinkedAgentId'si olan kullanıcıları port üzerinden al
            var linked = await registry.GetLinkedUsersAsync(ct);

            // Registry + linked users birleştir (ID'ye göre deduplikasyon)
            var registryIds = all.Select(a => a.Id).ToHashSet();
            var merged = all.Select(a => new { id = a.Id, displayName = a.DisplayName, isActive = a.IsActive })
                .Concat(linked
                    .Where(u => !registryIds.Contains(u.Id))
                    .Select(u => new { id = u.Id, displayName = u.DisplayName, isActive = u.IsActive }))
                .OrderBy(a => a.displayName)
                .ToList();

            return Results.Ok(new { count = merged.Count, items = merged });
        });

        // ─── Create ───
        group.MapPost("", (HumanAgentInput input, IHumanAgentRegistry registry) =>
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
            var created = registry.Create(agent);
            return Results.Created($"/agents/{created.Id}", created);
        });

        // ─── Get one ───
        group.MapGet("/{id}", (string id, IHumanAgentRegistry registry) =>
        {
            var a = registry.Get(id);
            return a is null ? Results.NotFound() : Results.Ok(a);
        });

        // ─── Update ───
        group.MapPut("/{id}", (string id, HumanAgentInput input, IHumanAgentRegistry registry) =>
        {
            var updated = registry.Update(id, input);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        // ─── Delete ───
        group.MapDelete("/{id}", (string id, IHumanAgentRegistry registry) =>
        {
            var ok = registry.Delete(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // ─── Reroute escalation (manual override) ───
        // /escalations/{id}/reroute → admin manuel olarak SuggestedAgentId değiştirebilir
        app.MapPost("/escalations/{id}/reroute", (string id, RerouteInput input,
            IEscalationSink sink, IHumanAgentRegistry registry) =>
        {
            var esc = sink.Get(id);
            if (esc is null) return Results.NotFound(new { error = "Escalation not found." });

            HumanAgent? newAgent = null;
            if (!string.IsNullOrWhiteSpace(input.AgentId))
            {
                newAgent = registry.Get(input.AgentId);
                if (newAgent is null) return Results.BadRequest(new { error = "Agent not found." });
            }

            // Eski temsilcinin yükünü azalt, yenisinin yükünü artır
            if (!string.IsNullOrWhiteSpace(esc.SuggestedAgentId))
                registry.DecrementLoad(esc.SuggestedAgentId);

            esc.SuggestedAgentId = newAgent?.Id;
            esc.SuggestedAgentName = newAgent?.DisplayName;
            esc.RoutingNote = string.IsNullOrWhiteSpace(input.Reason)
                ? $"Manuel atama: {newAgent?.DisplayName ?? "atama kaldırıldı"}."
                : input.Reason;

            if (newAgent is not null)
                registry.IncrementLoad(newAgent.Id);

            return Results.Ok(esc);
        });

        return app;
    }
}


