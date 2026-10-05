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

    /// <remarks>
    /// İptal, siparişin düştüğü stoğu geri verir. Eskiden yalnızca durum değişiyordu: stok
    /// <see cref="PlaceOrder"/>'da düşülüyor ama hiçbir yolda iade edilmiyordu, yani iptal
    /// edilen her siparişin adedi kalıcı olarak kayboluyordu.
    ///
    /// <para>
    /// İade TAM OLARAK BİR KEZ yapılmalı. Durum geçişi bu yüzden "oku → kontrol et → yaz"
    /// değil, koşullu tek bir <c>UPDATE ... WHERE status IN (İşleniyor, Kargolandı)</c>'dir:
    /// aynı iptal eşzamanlı yürütülürse (iki pod, tekrarlanan onay) Postgres ikinci
    /// UPDATE'i satır kilidinde bekletir, ilk commit'ten sonra koşulu yeniden değerlendirir ve
    /// 0 satır döner. Geçişi kazanamayan çağıran stoğa hiç dokunmaz. Geçiş ile iade aynı
    /// transaction'dadır; biri başarısız olursa ikisi de geri alınır.
    /// </para>
    ///
    /// <para>
    /// Kargolanmış bir siparişin iptalinde de stok iade edilir: iptal edilen gönderi
    /// göndericiye döner. Stok satırları, <see cref="StockDeduction"/> ile aynı sırada (ürün
    /// adına göre) güncellenir — eşzamanlı bir sipariş aynı ürünleri ters sırada kilitleyip
    /// deadlock üretmesin diye.
    /// </para>
    /// </remarks>
    public bool Cancel(string orderId, string reason)
    {
        if (!long.TryParse(orderId, out var id)) return false;

        using var probe = _dbFactory.CreateDbContext();
        var strategy = probe.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using var ctx = _dbFactory.CreateDbContext();
            using var tx = ctx.Database.BeginTransaction();

            var cancelledAt = DateTime.UtcNow;
            var claimed = ctx.Orders
                .Where(o => o.Code == id
                    && (o.Status == WellKnown.OrderStatuses.Processing
                        || o.Status == WellKnown.OrderStatuses.Shipped))
                .ExecuteUpdate(s => s
                    .SetProperty(o => o.Status, WellKnown.OrderStatuses.Cancelled)
                    .SetProperty(o => o.CancelledAt, cancelledAt)
                    .SetProperty(o => o.CancelReason, reason));

            if (claimed == 0) return false;

            var lines = ctx.OrderDetails
                .Where(d => d.OrderCode == id)
                .Select(d => new { d.ProductId, d.Product.Name, d.Quantity })
                .ToList();

            foreach (var line in lines.OrderBy(l => l.Name, StringComparer.Ordinal))
            {
                ctx.Products
                    .Where(p => p.Id == line.ProductId)
                    .ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock + line.Quantity));
            }

            tx.Commit();
            return true;
        });
    }

    public bool RequestReturn(string orderId, string reason)
    {
        if (!long.TryParse(orderId, out var id)) return false;
        using var ctx = _dbFactory.CreateDbContext();
        var e = ctx.Orders.FirstOrDefault(o => o.Code == id);
        if (e is null) return false;

        if (e.Status != WellKnown.OrderStatuses.Delivered)
            return false;

        // Politika "aldıkları ürünleri 14 gün içinde" der: süre TESLİMDEN başlar. Eskiden
        // sipariş tarihinden sayılıyordu; geç teslim edilen bir siparişin müşterisine fiilen
        // birkaç gün kalıyordu. Teslim tarihi olmayan eski kayıtlar sipariş tarihine düşer —
        // bilinen en erken, dolayısıyla en tutucu tarih.
        var windowStart = e.DeliveredAt ?? e.OrderDate;
        if ((DateTime.UtcNow - windowStart).TotalDays > 14)
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
    // ─── Fulfillment: durum geçişleri ───────────────────────────────────────

    /// <remarks>
    /// İptaldeki gibi "oku → kontrol et → yaz" değil, koşullu tek UPDATE: kargolama ile iptal eşzamanlı gelirse
    /// Postgres ikincisini satır kilidinde bekletir ve koşulu yeniden değerlendirir — iptal edilmiş sipariş
    /// kargolanmış görünmez, kargolanmış sipariş iptalde kaybolmaz. 0 satırda neden okunur (yok / zaten hedef
    /// durumda / geçersiz geçiş).
    /// </remarks>
    public OrderStatusUpdateResult MarkShipped(string orderId, string? carrier, string? trackingNumber, DateTime shippedAtUtc)
    {
        if (!long.TryParse(orderId, out var id)) return new OrderStatusUpdateResult(OrderStatusChange.NotFound, null);
        var at = DateTime.SpecifyKind(shippedAtUtc, DateTimeKind.Utc);
        using (var ctx = _dbFactory.CreateDbContext())
        {
            var updated = ctx.Orders
                .Where(o => o.Code == id && o.Status == WellKnown.OrderStatuses.Processing)
                .ExecuteUpdate(s => s
                    .SetProperty(o => o.Status, WellKnown.OrderStatuses.Shipped)
                    .SetProperty(o => o.ShippedAt, at)
                    .SetProperty(o => o.Carrier, carrier)
                    .SetProperty(o => o.TrackingNumber, trackingNumber));
            if (updated == 1) return new OrderStatusUpdateResult(OrderStatusChange.Updated, Get(orderId));
        }
        return Explain(orderId, WellKnown.OrderStatuses.Shipped);
    }

    public OrderStatusUpdateResult MarkDelivered(string orderId, DateTime deliveredAtUtc)
    {
        if (!long.TryParse(orderId, out var id)) return new OrderStatusUpdateResult(OrderStatusChange.NotFound, null);
        var at = DateTime.SpecifyKind(deliveredAtUtc, DateTimeKind.Utc);
        using (var ctx = _dbFactory.CreateDbContext())
        {
            var updated = ctx.Orders
                .Where(o => o.Code == id && o.Status == WellKnown.OrderStatuses.Shipped)
                .ExecuteUpdate(s => s
                    .SetProperty(o => o.Status, WellKnown.OrderStatuses.Delivered)
                    .SetProperty(o => o.DeliveredAt, at));
            if (updated == 1) return new OrderStatusUpdateResult(OrderStatusChange.Updated, Get(orderId));
        }
        return Explain(orderId, WellKnown.OrderStatuses.Delivered);
    }

    private OrderStatusUpdateResult Explain(string orderId, string targetStatus)
    {
        var current = Get(orderId);
        if (current is null) return new OrderStatusUpdateResult(OrderStatusChange.NotFound, null);
        return new OrderStatusUpdateResult(
            current.Status == targetStatus ? OrderStatusChange.Unchanged : OrderStatusChange.InvalidTransition, current);
    }

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
            ShippedAt = e.ShippedAt,
            Carrier = e.Carrier,
            TrackingNumber = e.TrackingNumber,
            DeliveredAt = e.DeliveredAt,
            ReturnRequestedAt = e.ReturnRequestedAt,
            ReturnReason = e.ReturnReason
        };
    }
}
