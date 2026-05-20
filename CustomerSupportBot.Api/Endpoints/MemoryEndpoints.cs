// Endpoints/MemoryEndpoints.cs
// Semantic memory dashboard / debug endpoint'leri (admin-only).

using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Api.Endpoints;

public static class MemoryEndpoints
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/memory");

        // GET /memory/stats — her collection için nokta sayısı
        group.MapGet("/stats", async (IMemoryPort port, CancellationToken ct) =>
        {
            if (!port.Enabled) return Results.Json(new { enabled = false });
            var episodic = await port.CountAsync(MemoryKind.Episodic, ct);
            var lessons = await port.CountAsync(MemoryKind.Lesson, ct);
            var knowledge = await port.CountAsync(MemoryKind.Knowledge, ct);
            var cfg = port.Config;
            return Results.Json(new
            {
                enabled = true,
                collections = new { episodic, lessons, knowledge },
                config = new
                {
                    embeddingModel = cfg.EmbeddingModel,
                    dimension = cfg.Dimension,
                    topK = cfg.TopK,
                    minScore = cfg.MinScore
                }
            });
        });

        // GET /memory/search?kind=knowledge&q=...&topK=5
        group.MapGet("/search", async (
            string q, IMemoryPort port,
            string kind = "knowledge", int? topK = null, CancellationToken ct = default) =>
        {
            if (!Enum.TryParse<MemoryKind>(kind, ignoreCase: true, out var k))
                return Results.BadRequest(new { error = "kind invalid; one of: episodic|lesson|knowledge" });

            var hits = await port.SearchAsync(k, q, topK, ct);
            return Results.Json(hits.Select(h => new
            {
                score = h.Score,
                doc = new
                {
                    h.Document.Id,
                    kind = h.Document.Kind.ToString(),
                    h.Document.Title,
                    h.Document.Source,
                    h.Document.SessionId,
                    text = h.Document.Text,
                    h.Document.CreatedAt,
                    h.Document.Tags
                }
            }));
        });

        // POST /memory/ingest — KB'yi yeniden tara (manuel tetikleme)
        group.MapPost("/ingest", async (IMemoryPort port, CancellationToken ct) =>
        {
            await port.IngestAsync(ct);
            return Results.Json(new { status = "ok" });
        });

        return app;
    }
}
