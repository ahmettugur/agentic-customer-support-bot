// Postgres/PostgresMessageFeedbackStore.cs
// IMessageFeedbackStore — kalıcı uygulama (analytics.message_feedback) + KVKK silme.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// Geri bildirim yorumu serbest metindir (kişisel veri içerebilir): oturum silinince satırlar da silinir
/// (<see cref="ISessionDataEraser"/>). Gözlem aracına (Langfuse) yazılmış skor da önce oradan kaldırılır ve hata
/// <b>fırlatılır</b>: DataPrivacyService depo hatasında oturumu yerinde bırakır, silme tekrarlanır — veri bir yerde
/// kalmışken "silindi" raporlanmaz.
/// </summary>
public sealed class PostgresMessageFeedbackStore(
    IDbContextFactory<CustomerSupportDbContext> dbFactory,
    IConversationScoreSink? scoreSink = null) : IMessageFeedbackStore, ISessionDataEraser
{
    public string Name => "message_feedback";

    private static DateTime Utc(DateTime v) =>
        v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc);

    private static MessageFeedback ToDomain(CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics.MessageFeedbackEntity e) => new()
    {
        SessionId = e.SessionId, MessageHash = e.MessageHash, Helpful = e.Helpful, Reason = e.Reason, Comment = e.Comment,
        TraceId = e.TraceId, CustomerId = e.CustomerId,
        CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc), UpdatedAt = DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)
    };

    public async Task UpsertAsync(MessageFeedback f, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var created = Utc(f.CreatedAt);
        var updated = Utc(f.UpdatedAt);
        // created_at yalnızca ilk eklemede yazılır; çakışmada korunur.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO analytics.message_feedback
                (session_id, message_hash, helpful, reason, comment, trace_id, customer_id, created_at, updated_at)
            VALUES ({f.SessionId}, {f.MessageHash}, {f.Helpful}, {f.Reason}, {f.Comment}, {f.TraceId}, {f.CustomerId}, {created}, {updated})
            ON CONFLICT (session_id, message_hash) DO UPDATE SET
                helpful = EXCLUDED.helpful, reason = EXCLUDED.reason, comment = EXCLUDED.comment,
                trace_id = EXCLUDED.trace_id, customer_id = EXCLUDED.customer_id, updated_at = EXCLUDED.updated_at
            """, ct);
    }

    public async Task RemoveAsync(string sessionId, string messageHash, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.MessageFeedback.Where(f => f.SessionId == sessionId && f.MessageHash == messageHash).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<MessageFeedback>> GetForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.MessageFeedback.AsNoTracking().Where(f => f.SessionId == sessionId).OrderBy(f => f.CreatedAt).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<MessageFeedback>> GetRecentAsync(int count, bool? helpful = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.MessageFeedback.AsNoTracking().AsQueryable();
        if (helpful is not null) query = query.Where(f => f.Helpful == helpful);
        var rows = await query.OrderByDescending(f => f.UpdatedAt).Take(count).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<MessageFeedback>> GetUnhelpfulSinceAsync(DateTime sinceUtc, int max, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Utc(sinceUtc);
        var rows = await db.MessageFeedback.AsNoTracking().Where(f => !f.Helpful && f.UpdatedAt >= since)
            .OrderByDescending(f => f.UpdatedAt).Take(max).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        var ids = sessionIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (scoreSink is not null)
        {
            var rows = await db.MessageFeedback.AsNoTracking().Where(f => ids.Contains(f.SessionId))
                .Select(f => new { f.SessionId, f.MessageHash }).ToListAsync(ct);
            foreach (var row in rows)
                await scoreSink.RemoveMessageFeedbackAsync(row.SessionId, row.MessageHash, ct);
        }

        return await db.MessageFeedback.Where(f => ids.Contains(f.SessionId)).ExecuteDeleteAsync(ct);
    }
}
