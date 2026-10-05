// Endpoints/SavedReplyEndpoints.cs
// Hazır yanıtlar.
//   GET    /saved-replies?q=          [Admin]          GET /agent/saved-replies?q=   [AdminOrAgent]
//   POST   /saved-replies             [Admin]
//   PUT    /saved-replies/{id}        [Admin]
//   DELETE /saved-replies/{id}        [Admin]
// Yetki ve hız sınırı çağıran grupta (Program.cs: adminScope / agentScope).

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class SavedReplyEndpoints
{
    public static IEndpointRouteBuilder MapAdminSavedReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/saved-replies", ListAsync);

        app.MapPost("/saved-replies", async (SavedReplyInput body, HttpContext http, ISavedReplyPort replies, CancellationToken ct) =>
        {
            var result = await replies.CreateAsync(body, http.User.FindFirst(ClaimTypes.Name)?.Value, ct);
            return result.Status == SavedReplyStatus.Ok
                ? Results.Created($"/saved-replies/{result.Reply!.Id}", result.Reply)
                : Failure(result);
        });

        app.MapPut("/saved-replies/{id}", async (string id, SavedReplyInput body, ISavedReplyPort replies, CancellationToken ct) =>
        {
            var result = await replies.UpdateAsync(id, body, ct);
            return result.Status == SavedReplyStatus.Ok ? Results.Ok(result.Reply) : Failure(result);
        });

        app.MapDelete("/saved-replies/{id}", async (string id, ISavedReplyPort replies, CancellationToken ct) =>
            await replies.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        return app;
    }

    /// <summary>Temsilci paneli — yalnız okuma.</summary>
    public static IEndpointRouteBuilder MapAgentSavedReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/saved-replies", ListAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(ISavedReplyPort replies, CancellationToken ct, string? q = null) =>
        Results.Ok(await replies.ListAsync(q, ct));

    private static IResult Failure(SavedReplyResult result) => result.Status switch
    {
        SavedReplyStatus.NotFound => Results.NotFound(new { error = "not_found", message = result.Error }),
        SavedReplyStatus.DuplicateShortcut => Results.Conflict(new { error = "duplicate_shortcut", message = result.Error }),
        _ => Results.BadRequest(new { error = "invalid", message = result.Error })
    };
}
