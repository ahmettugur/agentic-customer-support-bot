using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class OrderRepository : IOrderRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<OrderRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public string Create(OrderInfo order)
    {
        using var ctx = _dbFactory.CreateDbContext();

        var product = ctx.Products.First(p => p.Name == order.Product);

        var orderEntity = new OrderEntity
        {
            CustomerId = long.Parse(order.CustomerId),
            Status = order.Status,
            OrderDate = order.OrderDate.Kind == DateTimeKind.Utc
                ? order.OrderDate
                : DateTime.SpecifyKind(order.OrderDate, DateTimeKind.Utc)
        };
        ctx.Orders.Add(orderEntity);
        ctx.SaveChanges(); // Code DB tarafından üretilir, EF geri okur

        ctx.OrderDetails.Add(new OrderDetailEntity
        {
            OrderCode = orderEntity.Code,
            ProductId = product.Id,
            Quantity = order.Quantity
        });
        ctx.SaveChanges();
        return orderEntity.Code.ToString();
    }

    public OrderInfo? Get(string orderId)
    {
        if (!long.TryParse(orderId, out var id)) return null;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Orders
            .AsNoTracking()
            .Include(o => o.Details).ThenInclude(d => d.Product)
            .FirstOrDefault(o => o.Code == id);
        return e is null ? null : MapToModel(e);
    }

    public IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId)
    {
        if (!long.TryParse(customerId, out var cid)) return [];
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Orders
            .AsNoTracking()
            .Include(o => o.Details).ThenInclude(d => d.Product)
            .Where(o => o.CustomerId == cid)
            .OrderByDescending(o => o.OrderDate)
            .AsEnumerable()
            .Select(e => (e.Code.ToString(), MapToModel(e)))
            .ToList();
    }

    public (string OrderId, OrderInfo Order)? GetLast(string customerId)
    {
        if (!long.TryParse(customerId, out var cid)) return null;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Orders
            .AsNoTracking()
            .Include(o => o.Details).ThenInclude(d => d.Product)
            .Where(o => o.CustomerId == cid)
            .OrderByDescending(o => o.OrderDate)
            .FirstOrDefault();
        return e is null ? null : (e.Code.ToString(), MapToModel(e));
    }

    public bool Cancel(string orderId, string reason)
    {
        if (!long.TryParse(orderId, out var id)) return false;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Orders.FirstOrDefault(o => o.Code == id);
        if (e is null) return false;

        if (e.Status != WellKnown.OrderStatuses.Processing &&
            e.Status != WellKnown.OrderStatuses.Shipped)
            return false;

        e.Status = WellKnown.OrderStatuses.Cancelled;
        e.CancelledAt = DateTime.UtcNow;
        e.CancelReason = reason;
        ctx.SaveChanges();
        return true;
    }

    public bool RequestReturn(string orderId, string reason)
    {
        if (!long.TryParse(orderId, out var id)) return false;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Orders.FirstOrDefault(o => o.Code == id);
        if (e is null) return false;

        if (e.Status != WellKnown.OrderStatuses.Delivered)
            return false;

        if ((DateTime.UtcNow - e.OrderDate).TotalDays > 14)
            return false;

        e.Status = WellKnown.OrderStatuses.ReturnRequested;
        e.ReturnRequestedAt = DateTime.UtcNow;
        e.ReturnReason = reason;
        ctx.SaveChanges();
        return true;
    }

    private static OrderInfo MapToModel(OrderEntity e)
    {
        var detail = e.Details.FirstOrDefault();
        return new OrderInfo
        {
            Product = detail?.Product.Name ?? "",
            Quantity = detail?.Quantity ?? 0,
            CustomerId = e.CustomerId.ToString(),
            Status = e.Status,
            OrderDate = e.OrderDate,
            CancelledAt = e.CancelledAt,
            CancelReason = e.CancelReason,
            ReturnRequestedAt = e.ReturnRequestedAt,
            ReturnReason = e.ReturnReason
        };
    }
}
