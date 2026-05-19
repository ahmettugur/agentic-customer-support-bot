// Endpoints/PersonalizationEndpoints.cs
// Per-customer profil yönetim API'si — admin scope.

using CustomerSupportBot.Application.Services.Personalization;

namespace CustomerSupportBot.Api.Endpoints;

public static class PersonalizationEndpoints
{
    public static IEndpointRouteBuilder MapPersonalizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers").WithTags("Personalization");

        group.MapGet("/", (ICustomerProfileStore store, int? take) =>
        {
            return Results.Ok(new
            {
                count = store.Count,
                items = store.List(take ?? 100)
            });
        });

        group.MapGet("/{id}/profile", (string id, ICustomerProfileStore store) =>
        {
            var p = store.Get(id);
            return p is null ? Results.NotFound(new { customerId = id, found = false }) : Results.Ok(p);
        });

        group.MapPost("/{id}/profile/refresh", async (
            string id,
            CustomerProfileService service,
            CancellationToken ct) =>
        {
            var profile = await service.ConsolidateAsync(id, ct);
            return profile is null
                ? Results.NotFound(new { customerId = id, found = false })
                : Results.Ok(profile);
        });

        group.MapPut("/{id}/profile/note", (string id, AdminNoteInput input, ICustomerProfileStore store) =>
        {
            var p = store.GetOrCreate(id);
            p.AdminNote = string.IsNullOrWhiteSpace(input?.Note) ? null : input!.Note;
            store.Upsert(p);
            return Results.Ok(p);
        });

        group.MapDelete("/{id}/profile", (string id, ICustomerProfileStore store) =>
        {
            return store.Delete(id) ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
