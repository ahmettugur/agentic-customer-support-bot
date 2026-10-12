using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class ComplaintRepository : IComplaintRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<ComplaintRepository> _logger;

    public ComplaintRepository(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<ComplaintRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public string Create(ComplaintInfo complaint)
    {
        using var ctx = _dbFactory.CreateDbContext();
        var seq = ctx.Database
            .SqlQuery<long>($"SELECT nextval('catalog.complaint_seq') AS \"Value\"")
            .AsEnumerable()
            .First();

        var at = DateTime.SpecifyKind(complaint.CreatedAt == default ? DateTime.UtcNow : complaint.CreatedAt, DateTimeKind.Utc);
        ctx.Complaints.Add(new ComplaintEntity
        {
            Code = seq,
            OrderId = long.Parse(complaint.OrderId),
            CustomerId = long.Parse(complaint.CustomerId),
            Complaint = complaint.Complaint,
            Status = complaint.Status,
            CreatedAt = at,
            UpdatedAt = at,
            ApprovalId = complaint.ApprovalId
        });
        ctx.ComplaintEvents.Add(new ComplaintEventEntity
        {
            ComplaintCode = seq,
            At = at,
            Actor = WellKnown.Defaults.System,
            Kind = ComplaintEventKinds.Created,
            ToStatus = complaint.Status
        });
        ctx.SaveChanges();
        return seq.ToString();
    }

    public ComplaintInfo? Get(string complaintId)
    {
        if (!long.TryParse(complaintId, out var id)) return null;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Complaints.AsNoTracking().FirstOrDefault(c => c.Code == id);
        return e is null ? null : MapToModel(e);
    }

    public IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId)
    {
        if (!long.TryParse(orderId, out var oid)) return [];
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Complaints
            .AsNoTracking()
            .Where(c => c.OrderId == oid)
            .OrderBy(c => c.Code)
            .AsEnumerable()
            .Select(e => (e.Code.ToString(), MapToModel(e)))
            .ToList();
    }

    public IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId)
    {
        if (!long.TryParse(customerId, out var cid)) return [];
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Complaints
            .AsNoTracking()
            .Where(c => c.CustomerId == cid)
            .OrderBy(c => c.Code)
            .AsEnumerable()
            .Select(e => (e.Code.ToString(), MapToModel(e)))
            .ToList();
    }

    public async Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var q = Filter(ctx.Complaints.AsNoTracking(), query);
        var total = await q.CountAsync(ct);

        // Açıklar önce ve en eskiden yeniye; kapalılar sonra ve en yeniden eskiye. Koşullu anahtarlar
        // CASE WHEN'e çevrilir: her grupta diğer grubun anahtarı NULL olduğundan sıralamayı bozmaz.
        var closedOnly = WellKnown.ComplaintStatuses.IsClosed(query.Status);
        var ordered = closedOnly
            ? q.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Code)
            : q.OrderBy(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected)
               .ThenBy(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected
                   ? (DateTime?)null : c.CreatedAt)
               .ThenByDescending(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected
                   ? c.UpdatedAt : (DateTime?)null)
               .ThenBy(c => c.Code);

        var size = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(query.Page, 1);
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new ComplaintPage(rows.Select(e => new ComplaintListItem(e.Code.ToString(), MapToModel(e))).ToList(), total);
    }

    private static IQueryable<ComplaintEntity> Filter(IQueryable<ComplaintEntity> q, ComplaintQuery query)
    {
        if (query.Status == ComplaintQuery.OpenStatus)
            q = q.Where(c => c.Status == WellKnown.ComplaintStatuses.Pending || c.Status == WellKnown.ComplaintStatuses.InProgress);
        else if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(c => c.Status == query.Status);
        if (query.UnassignedOnly) q = q.Where(c => c.AssignedAgentId == null);
        if (query.AssignedAgentId is { } assigned) q = q.Where(c => c.AssignedAgentId == assigned);
        if (query.VisibleToAgentId is { } me) q = q.Where(c => c.AssignedAgentId == null || c.AssignedAgentId == me);
        return q;
    }

    public async Task<ComplaintCounts> CountOpenAsync(string? agentId, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var all = ctx.Complaints.AsNoTracking();
        var pending = await all.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.Pending, ct);
        var inProgress = await all.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.InProgress, ct);
        var mine = agentId is null
            ? 0
            : await all.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.InProgress && c.AssignedAgentId == agentId, ct);
        return new ComplaintCounts(pending, inProgress, mine);
    }

    public async Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(int max = 500, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.Complaints.AsNoTracking()
            .Where(c => c.Status == WellKnown.ComplaintStatuses.Pending || c.Status == WellKnown.ComplaintStatuses.InProgress)
            .OrderBy(c => c.CreatedAt)
            .Take(Math.Clamp(max, 1, 5000))
            .ToListAsync(ct);
        return rows.Select(e => new ComplaintListItem(e.Code.ToString(), MapToModel(e))).ToList();
    }

    public async Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(string complaintId, CancellationToken ct = default)
    {
        if (!long.TryParse(complaintId, out var code)) return [];
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.ComplaintEvents.AsNoTracking()
            .Where(e => e.ComplaintCode == code)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return rows.Select(e => new ComplaintEvent(DateTime.SpecifyKind(e.At, DateTimeKind.Utc), e.Actor, e.Kind,
            e.FromStatus, e.ToStatus, e.AssignedAgentId, e.Note)).ToList();
    }

    /// <summary>
    /// Koşullu geçiş: <c>status</c> VE <c>version</c> beklenen değerdeyse günceller ve olay satırını AYNI
    /// transaction'da yazar. Etkilenen satır 0 ise (başkası önce davrandı) hiçbir şey yazılmaz → Conflict.
    /// Execution strategy içinde çalışır: NpgsqlRetryingExecutionStrategy elle açılan transaction'ı başka türlü reddeder.
    /// </summary>
    public async Task<ComplaintTransitionOutcome> TryTransitionAsync(ComplaintTransition t, CancellationToken ct = default)
    {
        if (!long.TryParse(t.ComplaintId, out var code))
            return new ComplaintTransitionOutcome(ComplaintTransitionChange.NotFound, null);

        var at = DateTime.SpecifyKind(t.Event.At, DateTimeKind.Utc);
        await using var probe = await _dbFactory.CreateDbContextAsync(ct);
        var strategy = probe.Database.CreateExecutionStrategy();

        var updated = await strategy.ExecuteAsync(async () =>
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            var rows = await ctx.Complaints
                .Where(c => c.Code == code && c.Status == t.ExpectedStatus && c.Version == t.ExpectedVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, t.NewStatus)
                    .SetProperty(c => c.AssignedAgentId, t.AssignedAgentId)
                    .SetProperty(c => c.Resolution, c => t.Resolution ?? c.Resolution)
                    .SetProperty(c => c.UpdatedAt, at)
                    .SetProperty(c => c.Version, c => c.Version + 1), ct);
            if (rows != 1) return false;

            ctx.ComplaintEvents.Add(ToEntity(code, t.Event));
            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });

        var current = Get(t.ComplaintId);
        if (current is null) return new ComplaintTransitionOutcome(ComplaintTransitionChange.NotFound, null);
        if (!updated)
            _logger.LogInformation("[Complaint] Koşullu geçiş kaybetti (yarış ya da eski sürüm) | complaint={ComplaintId}", t.ComplaintId);
        return new ComplaintTransitionOutcome(
            updated ? ComplaintTransitionChange.Updated : ComplaintTransitionChange.Conflict, current);
    }

    public async Task<bool> AddEventAsync(string complaintId, ComplaintEvent evt, CancellationToken ct = default)
    {
        if (!long.TryParse(complaintId, out var code)) return false;
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        if (!await ctx.Complaints.AnyAsync(c => c.Code == code, ct)) return false;
        ctx.ComplaintEvents.Add(ToEntity(code, evt));
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static ComplaintEventEntity ToEntity(long code, ComplaintEvent e) => new()
    {
        ComplaintCode = code,
        At = DateTime.SpecifyKind(e.At, DateTimeKind.Utc),
        Actor = e.Actor.Length > 128 ? e.Actor[..128] : e.Actor,
        Kind = e.Kind,
        FromStatus = e.FromStatus,
        ToStatus = e.ToStatus,
        AssignedAgentId = e.AssignedAgentId,
        Note = e.Note
    };

    private static ComplaintInfo MapToModel(ComplaintEntity e) => new()
    {
        OrderId = e.OrderId.ToString(),
        CustomerId = e.CustomerId.ToString(),
        Complaint = e.Complaint,
        Status = e.Status,
        CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc),
        AssignedAgentId = e.AssignedAgentId,
        Resolution = e.Resolution,
        ApprovalId = e.ApprovalId,
        Version = e.Version
    };
}
