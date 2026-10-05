// Postgres/PostgresNotificationLedger.cs
// Bildirim defteri — benzersiz anahtar üzerinde atomik talep.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// <see cref="INotificationLedger"/>: <c>INSERT … ON CONFLICT DO NOTHING</c> — anahtarı ekleyebilen pod talebi
/// kazanır. "Önce oku sonra yaz" yapılmaz (iki pod aynı anda "yok" görüp ikisi de gönderirdi).
/// </summary>
public sealed class PostgresNotificationLedger(IDbContextFactory<CustomerSupportDbContext> dbFactory) : INotificationLedger
{
    public async Task<bool> TryClaimAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO notifications.sent_log (key, created_at) VALUES ({key}, {DateTime.UtcNow}) ON CONFLICT (key) DO NOTHING",
            ct);
        return inserted == 1;
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.NotificationLog.Where(n => n.Key == key).ExecuteDeleteAsync(ct);
    }
}
