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

    /// <summary>
    /// Siparişi ve <b>tüm</b> satırlarını yazar.
    /// </summary>
    /// <remarks>
    /// Başlık ve satırlar iki ayrı <c>SaveChanges</c> gerektirir (sipariş kodu DB tarafından
    /// üretilir ve satırların yabancı anahtarı odur), bu yüzden ikisi tek transaction'a
    /// alınır — aksi hâlde araya düşen bir hata satırsız bir "hayalet sipariş" bırakırdı.
    ///
    /// <para>
    /// Transaction, üretimde açık olan retry stratejisiyle (<c>EnableRetryOnFailure</c>)
    /// uyumlu olması için <c>CreateExecutionStrategy().Execute(...)</c> içinde çalıştırılır;
    /// aksi hâlde <c>NpgsqlRetryingExecutionStrategy</c> elle açılan transaction'ı reddeder.
    /// <see cref="DbContext"/> delegate'in İÇİNDE açılır: yeniden denemede taze bir
    /// change-tracker gerekir, yoksa ilk denemede eklenen entity'ler ikinci kez yazılırdı.
    /// </para>
    /// </remarks>
    public string Create(OrderInfo order)
    {
        if (order.Lines.Count == 0)
            throw new ArgumentException("Sipariş en az bir satır içermelidir.", nameof(order));

        using var probe = _dbFactory.CreateDbContext();
        var strategy = probe.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using var ctx = _dbFactory.CreateDbContext();
            using var tx = ctx.Database.BeginTransaction();

            var names = order.Lines.Select(l => l.Product).ToList();
            var products = ctx.Products
                .Where(p => names.Contains(p.Name))
                .ToDictionary(p => p.Name, p => p.Id);

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

            foreach (var line in order.Lines)
            {
                ctx.OrderDetails.Add(new OrderDetailEntity
                {
                    OrderCode = orderEntity.Code,
                    ProductId = products[line.Product],
                    Quantity = line.Quantity
                });
            }

            ctx.SaveChanges();
            tx.Commit();
            return orderEntity.Code.ToString();
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Create"/> ile aynı transaction düzeni, tek farkla: stok düşümü de AYNI
    /// transaction'ın içindedir. Herhangi bir adım başarısız olursa hiçbiri kalıcı olmaz.
    /// </remarks>
    public OrderPlacementResult PlaceOrder(OrderInfo order)
    {
        if (order.Lines.Count == 0)
            throw new ArgumentException("Sipariş en az bir satır içermelidir.", nameof(order));

        using var probe = _dbFactory.CreateDbContext();
        var strategy = probe.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using var ctx = _dbFactory.CreateDbContext();
            using var tx = ctx.Database.BeginTransaction();

            // Stok ÖNCE düşülür: yetersizse sipariş hiç yazılmaz ve rollback ile
            // kısmen düşülmüş satırlar da geri alınır.
            var deduction = StockDeduction.TryDeduct(ctx, order.Lines, _logger);
            if (!deduction.Success)
                return OrderPlacementResult.OutOfStock(deduction);

            var names = order.Lines.Select(l => l.Product).ToList();
            var products = ctx.Products
                .Where(p => names.Contains(p.Name))
                .ToDictionary(p => p.Name, p => p.Id);

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

            foreach (var line in order.Lines)
            {
                ctx.OrderDetails.Add(new OrderDetailEntity
                {
                    OrderCode = orderEntity.Code,
                    ProductId = products[line.Product],
                    Quantity = line.Quantity
                });
            }

            ctx.SaveChanges();
            tx.Commit();
            return OrderPlacementResult.Placed(orderEntity.Code.ToString());
        });
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

    /// <remarks>
    /// Satırların tamamı okunur. Eskiden burada <c>Details.FirstOrDefault()</c> vardı —
    /// şema baştan beri çok satırlıydı ama model tek satıra düşürdüğü için ikinci ve
    /// sonraki ürünler sessizce kayboluyordu.
    /// </remarks>
    private static OrderInfo MapToModel(OrderEntity e)
    {
        return new OrderInfo
        {
            Lines = e.Details
                .OrderBy(d => d.Product.Name, StringComparer.Ordinal)
                .Select(d => new OrderLine(d.Product.Name, d.Quantity))
                .ToList(),
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
