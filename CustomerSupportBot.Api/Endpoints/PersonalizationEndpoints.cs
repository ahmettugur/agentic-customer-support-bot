// Endpoints/PersonalizationEndpoints.cs
// Per-customer profil yönetim API'si — admin scope.

using CustomerSupportBot.Application.Ports.Driving;

namespace CustomerSupportBot.Api.Endpoints;

public static class PersonalizationEndpoints
{
    public static IEndpointRouteBuilder MapPersonalizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers").WithTags("Personalization");

        group.MapGet("/", (IPersonalizationPort port, int? take) =>
        {
            var (count, items) = port.GetProfiles(take ?? 100);
            return Results.Ok(new { count, items });
        });

        group.MapGet("/{id}/profile", (string id, IPersonalizationPort port) =>
        {
            var p = port.GetProfile(id);
            return p is null ? Results.NotFound(new { customerId = id, found = false }) : Results.Ok(p);
        });

        group.MapPost("/{id}/profile/refresh", async (string id, IPersonalizationPort port, CancellationToken ct) =>
        {
            var profile = await port.RefreshProfileAsync(id, ct);
            return profile is null
                ? Results.NotFound(new { customerId = id, found = false })
                : Results.Ok(profile);
        });

        group.MapPut("/{id}/profile/note", (string id, AdminNoteInput input, IPersonalizationPort port) =>
        {
            var p = port.SetAdminNote(id, input?.Note);
            return Results.Ok(p);
        });

        group.MapDelete("/{id}/profile", (string id, IPersonalizationPort port) =>
        {
            return port.DeleteProfile(id) ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
