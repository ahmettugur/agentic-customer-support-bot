// Postgres/PostgresConversationExcerptStore.cs
// chat.messages üzerinden zamana göre kesilmiş mesaj okuma. Önbellekli oturum geçmişinde zaman bilgisi yoktur.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresConversationExcerptStore(IDbContextFactory<CustomerSupportDbContext> dbFactory) : IConversationExcerptStore
{
    public async Task<IReadOnlyList<ConversationMessage>> GetBeforeAsync(
        string sessionId, DateTime beforeUtc, int count, CancellationToken ct = default)
    {
        var before = DateTime.SpecifyKind(beforeUtc, DateTimeKind.Utc);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId && m.CreatedAt < before)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .Select(m => new { m.Role, m.Text })
            .ToListAsync(ct);

        rows.Reverse();
        return rows.Select(m => new ConversationMessage(
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ? ConversationRoles.User : ConversationRoles.Assistant,
            m.Text)).ToList();
    }
}
