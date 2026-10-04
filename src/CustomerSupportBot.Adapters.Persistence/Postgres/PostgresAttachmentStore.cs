// Postgres/PostgresAttachmentStore.cs
// Sohbet fotoğrafları — doğrudan PostgreSQL (cache yok: kayıtlar büyük ve seyrek okunur).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// <see cref="IAttachmentStore"/>'un PostgreSQL uygulaması. Diğer store'ların aksine bellek içi
/// cache tutmaz: görüntü verisi büyüktür, her pod'da kopyası tutulmamalı; okumalar seyrektir
/// (tur başına bir liste, temsilcinin açtığı her fotoğraf için bir kayıt). Listeleme görüntü
/// verisini OKUMAZ — yalnızca meta sütunları seçilir.
/// </summary>
public sealed class PostgresAttachmentStore(IDbContextFactory<CustomerSupportDbContext> dbFactory) : IAttachmentStore
{
    public async Task SaveAsync(ChatAttachment attachment, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attachments.Add(new AttachmentEntity
        {
            Id = attachment.Id,
            SessionId = attachment.SessionId,
            CustomerId = attachment.CustomerId,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.Data.Length,
            Data = attachment.Data,
            Description = attachment.Description,
            SentAt = attachment.SentAt,
            ApprovalId = attachment.ApprovalId,
            CreatedAt = attachment.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ChatAttachment?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return e is null ? null : new ChatAttachment
        {
            Id = e.Id,
            SessionId = e.SessionId,
            CustomerId = e.CustomerId,
            ContentType = e.ContentType,
            Data = e.Data,
            Description = e.Description,
            SentAt = e.SentAt,
            ApprovalId = e.ApprovalId,
            CreatedAt = e.CreatedAt
        };
    }

    public async Task<IReadOnlyList<ChatAttachment>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Attachments.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ChatAttachment
            {
                Id = a.Id,
                SessionId = a.SessionId,
                CustomerId = a.CustomerId,
                ContentType = a.ContentType,
                Description = a.Description,
                SentAt = a.SentAt,
                ApprovalId = a.ApprovalId,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task MarkSentAsync(IReadOnlyCollection<string> ids, DateTime sentAt, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Attachments
            .Where(a => ids.Contains(a.Id) && a.SentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, sentAt), ct);
    }

    public async Task<bool> DeleteUnsentAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Koşul DELETE'in içinde: gönderimle yarışan bir silme, gönderilmiş fotoğrafı silemez.
        return await db.Attachments.Where(a => a.Id == id && a.SentAt == null).ExecuteDeleteAsync(ct) > 0;
    }

    public async Task LinkToApprovalAsync(IReadOnlyCollection<string> ids, string approvalId, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Tek UPDATE; "approval_id IS NULL" koşulu eşzamanlı iki onayın aynı fotoğrafı birbirinin
        // elinden almasını önler — ilk bağlayan kazanır.
        await db.Attachments
            .Where(a => ids.Contains(a.Id) && a.ApprovalId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ApprovalId, approvalId), ct);
    }
}
