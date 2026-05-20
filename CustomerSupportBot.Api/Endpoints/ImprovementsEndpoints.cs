// Endpoints/ImprovementsEndpoints.cs
// Self-improving loop için admin endpoint'leri.

using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Api.Endpoints;

public static class ImprovementsEndpoints
{
    public static IEndpointRouteBuilder MapImprovementsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/improvements");

        group.MapPost("/mine", async (IImprovementsPort port, CancellationToken ct) =>
            Results.Json(await port.MineAsync(ct)));

        group.MapGet("/", (IImprovementsPort port, string? status = null) =>
        {
            LessonStatus? st = Enum.TryParse<LessonStatus>(status, ignoreCase: true, out var parsed)
                ? parsed : null;
            return Results.Json(port.GetLessons(st));
        });

        group.MapGet("/proposed", (IImprovementsPort port) =>
            Results.Json(port.GetLessons(LessonStatus.Proposed)));

        group.MapGet("/{id}", (string id, IImprovementsPort port) =>
        {
            var l = port.GetLesson(id);
            return l == null ? Results.NotFound() : Results.Json(l);
        });

        group.MapPost("/{id}/approve",
            async (string id, ImprovementDecision? body, IImprovementsPort port, CancellationToken ct) =>
        {
            var ok = await port.ApproveAsync(id,
                body?.DecidedBy ?? WellKnown.Defaults.Admin, body?.Reason, ct);
            return ok
                ? Results.Json(new { id, status = "approved" })
                : Results.NotFound(new { error = "Lesson bulunamadı veya zaten karara bağlanmış." });
        });

        group.MapPost("/{id}/reject",
            (string id, ImprovementDecision? body, IImprovementsPort port) =>
        {
            var ok = port.Reject(id, body?.DecidedBy ?? WellKnown.Defaults.Admin, body?.Reason);
            return ok
                ? Results.Json(new { id, status = "rejected" })
                : Results.NotFound(new { error = "Lesson bulunamadı veya zaten karara bağlanmış." });
        });

        return app;
    }
}
