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

        ctx.Complaints.Add(new ComplaintEntity
        {
            Code = seq,
            OrderId = long.Parse(complaint.OrderId),
            CustomerId = long.Parse(complaint.CustomerId),
            Complaint = complaint.Complaint,
            Status = complaint.Status
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

    private static ComplaintInfo MapToModel(ComplaintEntity e) => new()
    {
        OrderId = e.OrderId.ToString(),
        CustomerId = e.CustomerId.ToString(),
        Complaint = e.Complaint,
        Status = e.Status
    };
}
