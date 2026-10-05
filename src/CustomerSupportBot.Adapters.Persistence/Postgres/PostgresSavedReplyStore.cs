// Postgres/PostgresSavedReplyStore.cs
// Hazır yanıtlar — doğrudan PostgreSQL (önbellek yok: tablo küçük, liste yalnız seçici açılınca okunur).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresSavedReplyStore(IDbContextFactory<CustomerSupportDbContext> dbFactory) : ISavedReplyStore
{
    public async Task<IReadOnlyList<SavedReply>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.SavedReplies.AsNoTracking().ToListAsync(ct)).Select(ToDomain).ToList();
    }

    public async Task<SavedReply?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.SavedReplies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return e is null ? null : ToDomain(e);
    }

    public async Task<bool> ShortcutExistsAsync(string shortcut, string? exceptId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SavedReplies.AnyAsync(r => r.Shortcut == shortcut && r.Id != exceptId, ct);
    }

    public async Task AddAsync(SavedReply reply, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.SavedReplies.Add(ToEntity(reply));
        await SaveAsync(db, reply.Shortcut, ct);
    }

    public async Task<bool> UpdateAsync(SavedReply reply, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.SavedReplies.FirstOrDefaultAsync(r => r.Id == reply.Id, ct);
        if (e is null) return false;
        e.Title = reply.Title;
        e.Body = reply.Body;
        e.Shortcut = reply.Shortcut;
        e.UpdatedAt = reply.UpdatedAt;
        await SaveAsync(db, reply.Shortcut, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.SavedReplies.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (e is null) return false;
        db.SavedReplies.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Kısayol benzersiz indeksini ihlal eden yazım (eşzamanlı ekleme yarışı) alan istisnasına çevrilir.</summary>
    private static async Task SaveAsync(CustomerSupportDbContext db, string? shortcut, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SavedReplyConfiguration.ShortcutIndexName })
        {
            throw new SavedReplyShortcutConflictException(shortcut ?? "");
        }
    }

    private static SavedReply ToDomain(SavedReplyEntity e) => new()
    {
        Id = e.Id, Title = e.Title, Body = e.Body, Shortcut = e.Shortcut,
        CreatedBy = e.CreatedBy, CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt
    };

    private static SavedReplyEntity ToEntity(SavedReply r) => new()
    {
        Id = r.Id, Title = r.Title, Body = r.Body, Shortcut = r.Shortcut,
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}
