// Endpoints/MemoryEndpoints.cs
// Semantic memory dashboard / debug endpoint'leri (admin-only).

using CustomerSupportBot.Api.Models.Memory;
using CustomerSupportBot.Api.Services.Memory;

namespace CustomerSupportBot.Api.Endpoints;

public static class MemoryEndpoints
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/memory");

        // GET /memory/stats — her collection için nokta sayısı
        group.MapGet("/stats", async (SemanticMemoryService mem, CancellationToken ct) =>
        {
            if (!mem.Enabled) return Results.Json(new { enabled = false });
            var episodic = await mem.CountAsync(MemoryKind.Episodic, ct);
            var lessons = await mem.CountAsync(MemoryKind.Lesson, ct);
            var knowledge = await mem.CountAsync(MemoryKind.Knowledge, ct);
            return Results.Json(new
            {
                enabled = true,
                collections = new { episodic, lessons, knowledge },
                config = new
                {
                    embeddingModel = mem.Options.Embedding.Model,
                    dimension = mem.Options.Embedding.Dimension,
                    topK = mem.Options.Retrieval.TopK,
                    minScore = mem.Options.Retrieval.MinScore
                }
            });
        });

        // GET /memory/search?kind=knowledge&q=...&topK=5
        group.MapGet("/search", async (
            string q, SemanticMemoryService mem,
            string kind = "knowledge", int? topK = null, CancellationToken ct = default) =>
        {
            if (!Enum.TryParse<MemoryKind>(kind, ignoreCase: true, out var k))
                return Results.BadRequest(new { error = "kind invalid; one of: episodic|lesson|knowledge" });

            var hits = await mem.SearchAsync(k, q, topK: topK, ct: ct);
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
        group.MapPost("/ingest", async (KnowledgeBaseIngestor ingestor, CancellationToken ct) =>
        {
            await ingestor.StartAsync(ct);
            return Results.Json(new { status = "ok" });
        });

        return app;
    }
}
