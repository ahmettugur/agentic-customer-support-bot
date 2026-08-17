// Endpoints/MemoryEndpoints.cs
// Semantic memory dashboard / debug endpoint'leri (admin-only).

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
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

        MapArticleEndpoints(group);

        return app;
    }

    /// <summary>Panelden yönetilen bilgi tabanı makaleleri — CRUD.</summary>
    private static void MapArticleEndpoints(RouteGroupBuilder group)
    {
        var articles = group.MapGroup("/articles");

        articles.MapGet("/", async (IKnowledgeBasePort kb, CancellationToken ct) =>
            Results.Json((await kb.ListAsync(ct)).Select(ToDto)));

        articles.MapGet("/{id}", async (string id, IKnowledgeBasePort kb, CancellationToken ct) =>
        {
            var article = await kb.GetAsync(id, ct);
            return article is null
                ? Results.NotFound(new { error = "article_not_found" })
                : Results.Json(ToDto(article));
        });

        articles.MapPost("/", async (
            KnowledgeArticleInput body, ClaimsPrincipal user, IKnowledgeBasePort kb, CancellationToken ct) =>
        {
            if (Validate(body) is { } error) return error;

            var result = await kb.CreateAsync(
                body.Title, body.Content, body.Category, body.IsPublished, ActorOf(user), ct);

            return Results.Json(ToSaveDto(result));
        });

        articles.MapPut("/{id}", async (
            string id, KnowledgeArticleInput body, ClaimsPrincipal user, IKnowledgeBasePort kb, CancellationToken ct) =>
        {
            if (Validate(body) is { } error) return error;

            var result = await kb.UpdateAsync(
                id, body.Title, body.Content, body.Category, body.IsPublished, ActorOf(user), ct);

            return result is null
                ? Results.NotFound(new { error = "article_not_found" })
                : Results.Json(ToSaveDto(result));
        });

        articles.MapDelete("/{id}", async (string id, IKnowledgeBasePort kb, CancellationToken ct) =>
            await kb.DeleteAsync(id, ct)
                ? Results.Json(new { id, status = "deleted" })
                : Results.NotFound(new { error = "article_not_found" }));
    }

    // ─── helpers ───

    /// <summary>Düzenleyen, istemci gövdesinden değil doğrulanmış kimlikten alınır — denetim izi sahtelenemesin.</summary>
    private static string ActorOf(ClaimsPrincipal user)
        => user.Identity?.Name is { Length: > 0 } name ? name : WellKnown.Defaults.Admin;

    private static IResult? Validate(KnowledgeArticleInput body)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
            return Results.BadRequest(new { error = "title_required" });

        if (body.Title.Trim().Length > TitleMaxLength)
            return Results.BadRequest(new { error = "title_too_long", maxLength = TitleMaxLength });

        if (string.IsNullOrWhiteSpace(body.Content))
            return Results.BadRequest(new { error = "content_required" });

        if (body.Category is { Length: > 0 } && body.Category.Trim().Length > CategoryMaxLength)
            return Results.BadRequest(new { error = "category_too_long", maxLength = CategoryMaxLength });

        return null;
    }

    // knowledge.articles kolon sınırlarıyla hizalı — aşarsa DB hatası yerine 400 dönmeli.
    private const int TitleMaxLength = 256;
    private const int CategoryMaxLength = 64;

    private static object ToDto(KnowledgeArticle a) => new
    {
        a.Id,
        a.Title,
        a.Content,
        a.Category,
        a.IsPublished,
        a.IndexedChunkCount,
        a.CreatedAt,
        a.UpdatedAt,
        a.UpdatedBy
    };

    private static object ToSaveDto(KnowledgeArticleSaveResult r) => new
    {
        article = ToDto(r.Article),
        indexed = r.Indexed,
        warning = r.Warning
    };
}

/// <summary>Makale oluşturma/güncelleme gövdesi.</summary>
public sealed record KnowledgeArticleInput(
    string Title,
    string Content,
    string? Category,
    bool IsPublished);
