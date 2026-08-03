// Services/Persistence/PostgresKnowledgeArticleStore.cs
// IKnowledgeArticleStore'un PostgreSQL adaptörü.
//
// PostgresLessonStore'dan farklı olarak in-memory cache yok: makaleler nadiren
// (insan eliyle) değişir ve okuma yolları zaten async. Cache eklemek burada
// tutarlılık riskini karşılıksız artırırdı.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Knowledge;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresKnowledgeArticleStore : IKnowledgeArticleStore
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;

    public PostgresKnowledgeArticleStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<KnowledgeArticle>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.KnowledgeArticles
            .AsNoTracking()
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeArticle>> GetPublishedAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task UpsertAsync(KnowledgeArticle article, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == article.Id, ct);

        if (existing is null)
        {
            db.KnowledgeArticles.Add(ToEntity(article));
        }
        else
        {
            existing.Title = article.Title;
            existing.Content = article.Content;
            existing.Category = article.Category;
            existing.IsPublished = article.IsPublished;
            existing.IndexedChunkCount = article.IndexedChunkCount;
            existing.UpdatedAt = article.UpdatedAt;
            existing.UpdatedBy = article.UpdatedBy;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (existing is null) return false;

        db.KnowledgeArticles.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static KnowledgeArticleEntity ToEntity(KnowledgeArticle a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Content = a.Content,
        Category = a.Category,
        IsPublished = a.IsPublished,
        IndexedChunkCount = a.IndexedChunkCount,
        CreatedAt = a.CreatedAt == default ? DateTime.UtcNow : a.CreatedAt,
        UpdatedAt = a.UpdatedAt == default ? DateTime.UtcNow : a.UpdatedAt,
        UpdatedBy = a.UpdatedBy
    };

    private static KnowledgeArticle ToDomain(KnowledgeArticleEntity e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Content = e.Content,
        Category = e.Category,
        IsPublished = e.IsPublished,
        IndexedChunkCount = e.IndexedChunkCount,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        UpdatedBy = e.UpdatedBy
    };
}
