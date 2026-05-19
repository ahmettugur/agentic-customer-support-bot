// Endpoints/ImprovementsEndpoints.cs
// Self-improving loop için admin endpoint'leri.
//   POST /improvements/mine         — düşük puanlı / hatalı trace'leri tara, lesson aday üret
//   GET  /improvements              — tüm lesson'lar (status filter optional)
//   GET  /improvements/proposed     — sadece pending
//   POST /improvements/{id}/approve — approve + Qdrant'a yaz
//   POST /improvements/{id}/reject  — reddet
//   GET  /improvements/{id}         — tek lesson detayı

using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Api.Endpoints;

public static class ImprovementsEndpoints
{
    public sealed record ImprovementDecision(string? DecidedBy, string? Reason);

    public static IEndpointRouteBuilder MapImprovementsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/improvements");

        group.MapPost("/mine", async (LessonMiner miner, CancellationToken ct) =>
        {
            var report = await miner.MineAsync(ct);
            return Results.Json(report);
        });

        group.MapGet("/", (ILessonStore store, string? status = null) =>
        {
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<LessonStatus>(status, ignoreCase: true, out var st))
                return Results.Json(store.GetByStatus(st));
            return Results.Json(store.GetAll());
        });

        group.MapGet("/proposed", (ILessonStore store) =>
            Results.Json(store.GetByStatus(LessonStatus.Proposed)));

        group.MapGet("/{id}", (string id, ILessonStore store) =>
        {
            var l = store.Get(id);
            return l == null ? Results.NotFound() : Results.Json(l);
        });

        group.MapPost("/{id}/approve",
            async (string id, ImprovementDecision? body, LessonMiner miner, CancellationToken ct) =>
        {
            var ok = await miner.ApproveAsync(id,
                body?.DecidedBy ?? WellKnown.Defaults.Admin, body?.Reason, ct);
            return ok
                ? Results.Json(new { id, status = "approved" })
                : Results.NotFound(new { error = "Lesson bulunamadı veya zaten karara bağlanmış." });
        });

        group.MapPost("/{id}/reject",
            (string id, ImprovementDecision? body, LessonMiner miner) =>
        {
            var ok = miner.Reject(id, body?.DecidedBy ?? WellKnown.Defaults.Admin, body?.Reason);
            return ok
                ? Results.Json(new { id, status = "rejected" })
                : Results.NotFound(new { error = "Lesson bulunamadı veya zaten karara bağlanmış." });
        });

        return app;
    }
}

