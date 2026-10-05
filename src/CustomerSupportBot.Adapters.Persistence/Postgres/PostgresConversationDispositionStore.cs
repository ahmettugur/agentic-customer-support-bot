// Postgres/PostgresConversationDispositionStore.cs
// Konuşma kapanış kayıtları — doğrudan PostgreSQL (önbellek yok: yazım seyrek, okuma panel/analitikten).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresConversationDispositionStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IConversationDispositionStore, ISessionDataEraser
{
    public string Name => "conversation-dispositions";

    public async Task AddAsync(ConversationDisposition disposition, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ConversationDispositions.Add(new ConversationDispositionEntity
        {
            Id = disposition.Id, SessionId = disposition.SessionId, ReasonCode = disposition.ReasonCode,
            Tags = [.. disposition.Tags], Note = disposition.Note, ClosedBy = disposition.ClosedBy,
            ClosedAt = DateTime.SpecifyKind(disposition.ClosedAt, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationDisposition>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.ConversationDispositions.AsNoTracking()
                .Where(d => d.SessionId == sessionId)
                .OrderBy(d => d.ClosedAt)
                .ToListAsync(ct))
            .Select(e => new ConversationDisposition
            {
                Id = e.Id, SessionId = e.SessionId, ReasonCode = e.ReasonCode, Tags = [.. e.Tags], Note = e.Note,
                ClosedBy = e.ClosedBy, ClosedAt = e.ClosedAt
            })
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByReasonAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ConversationDispositions
            .GroupBy(d => d.ReasonCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public async Task<IReadOnlyList<TagUsage>> TopTagsAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Database.SqlQuery<TagRow>($"""
            SELECT tag AS "Tag", count(*)::int AS "Count"
            FROM chat.conversation_dispositions, unnest(tags) AS tag
            GROUP BY tag
            ORDER BY count(*) DESC, tag
            LIMIT {limit}
            """).ToListAsync(ct);
        return rows.Select(r => new TagUsage(r.Tag, r.Count)).ToList();
    }

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        var ids = sessionIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ConversationDispositions.Where(d => ids.Contains(d.SessionId)).ExecuteDeleteAsync(ct);
    }

    private sealed record TagRow(string Tag, int Count);
}
