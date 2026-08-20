using System.ComponentModel;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

/// <summary>
/// Sipariş yönetimi araçları uygulama servisi.
/// </summary>
public sealed class OrderToolsService : IOrderToolsService
{
    private readonly IOrderRepository _orders;
    private readonly IProductCatalogRepository _products;
    private readonly ICustomerRepository _customers;
    private readonly SideEffectIdempotencyCache _idempotency;

    public OrderToolsService(
        IOrderRepository orders,
        IProductCatalogRepository products,
        ICustomerRepository customers,
        SideEffectIdempotencyCache? idempotency = null)
    {
        _orders = orders;
        _products = products;
        _customers = customers;
        _idempotency = idempotency ?? new SideEffectIdempotencyCache();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sıralama kasıtlı: ucuz ve yan etkisiz kontroller (biçim, müşteri, katalog) önce
    /// çalışır; stok yalnızca her şey geçerliyse düşülür. Böylece geçersiz bir talep
    /// stoğa hiç dokunmaz.
    /// </remarks>
    [Description("Yeni sipariş oluşturur. Tek siparişte birden fazla ürün satırı olabilir. " +
                 "Sonuç ToolResult olarak döner.")]
    public ToolResult OrderPlacementTool(
        [Description("Sipariş satırları — her biri bir ürün adı ve adet")] IReadOnlyList<OrderLineRequest> lines,
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return ToolResult.ValidationError(
                "Sipariş için müşteri kimlik numarası gerekli.",
                WellKnown.ToolParameterNames.CustomerId);

        if (lines is null || lines.Count == 0)
            return ToolResult.ValidationError(
                "Sipariş için en az bir ürün satırı gerekli: her satırda ürün adı ve adet olmalı.",
                WellKnown.ToolParameterNames.Lines);

        if (lines.Any(l => string.IsNullOrWhiteSpace(l.ProductName)))
            return ToolResult.ValidationError(
                "Sipariş satırlarının hepsinde ürün adı olmalı.",
                WellKnown.ToolParameterNames.ProductName);

        // Adet hatasını satır bazında bildir — "hangi üründe" bilgisi olmadan kullanıcı
        // 5 satırlık bir siparişte hatanın nerede olduğunu bulamaz.
        var badQuantities = lines.Where(l => l.Quantity <= 0).Select(l => l.ProductName).ToList();
        if (badQuantities.Count > 0)
            return ToolResult.ValidationError(
                $"Adet en az 1 olmalı — hatalı satır(lar): {string.Join(", ", badQuantities)}.",
                WellKnown.ToolParameterNames.Quantity);

        if (!long.TryParse(customerId, out var customerIdLong)
            || !_customers.Exists(customerIdLong))
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.CustomerNotFound,
                $"'{customerId}' kimlik numaralı müşteri sistemde kayıtlı değil. Lütfen müşteri numaranızı kontrol edin.");

        // Katalog çözümlemesi: TÜM satırlar denenir, bulunamayanlar tek seferde bildirilir.
        // İlk hatada durulsaydı kullanıcı, çok ürünlü bir siparişteki eksikleri tur tur
        // öğrenirdi (ping-pong).
        var resolved = new List<OrderLine>(lines.Count);
        var notFound = new List<string>();
        foreach (var line in lines)
        {
            var product = _products.FindProduct(line.ProductName);
            if (product is null) notFound.Add(line.ProductName);
            else resolved.Add(new OrderLine(product.Name, line.Quantity));
        }

        if (notFound.Count > 0)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                notFound.Count == 1
                    ? $"Üzgünüz, '{notFound[0]}' ürünümüzün kataloğunda bulunmamaktadır."
                    : $"Üzgünüz, şu ürünler kataloğumuzda bulunmamaktadır: {string.Join(", ", notFound)}.");

        // Aynı ürün birden fazla satırda geçiyorsa tek satırda toplanır. Zorunlu:
        // order_details tablosunun birincil anahtarı (order_code, product_id) — tekrar eden
        // ürün ikinci INSERT'te ihlal verirdi. Sıralama, idempotency imzasının satır
        // sırasından bağımsız olmasını sağlar.
        var merged = resolved
            .GroupBy(l => l.Product, StringComparer.Ordinal)
            .Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(l => l.Product, StringComparer.Ordinal)
            .ToList();

        // Mükerrer çağrı koruması — stok düşülmeden ve sipariş yazılmadan ÖNCE.
        // İmza kanonik ürün adları üzerinden kurulur, böylece "kahve"/"Kahve" aynı sayılır.
        var signature = new object?[] { customerId, FormatLines(merged) };
        if (_idempotency.TryGetRecent(WellKnown.ToolNames.OrderPlacement, signature, out var recent))
        {
            return ToolResult.Ok(
                message: $"Bu siparişi az önce oluşturmuştum — sipariş numarası: {recent.EntityId}. " +
                         "Mükerrer kayıt oluşturmadım. Gerçekten ikinci bir sipariş istiyorsanız lütfen açıkça belirtin.",
                data: new
                {
                    orderId = recent.EntityId,
                    lines = ToLineData(merged),
                    totalQuantity = merged.Sum(l => l.Quantity),
                    customerId,
                    status = WellKnown.OrderStatuses.Processing,
                    duplicate = true
                },
                confidence: 0.9);
        }

        // Stok düşümü ve siparişin yazılması TEK transaction'dadır. Ayrı yapıldıklarında
        // aradaki bir hata (DB kesintisi, retry tükenmesi, pod'un ölmesi) stoğu düşülmüş ama
        // karşılığında hiçbir sipariş oluşmamış hâlde bırakıyordu — hiçbir yerde hata
        // görünmeden, ürün stoğu kalıcı olarak azalarak.
        var placement = _orders.PlaceOrder(new OrderInfo
        {
            Lines      = merged,
            CustomerId = customerId,
            Status     = WellKnown.OrderStatuses.Processing,
            OrderDate  = DateTime.Now
        });

        if (placement.OrderId is null)
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.StockInsufficient,
                "Stok yetersiz, sipariş oluşturulamadı: " +
                string.Join("; ", placement.Stock.Shortages.Select(s =>
                    $"{s.Product} için {s.Requested} adet istendi, stokta {s.Available} adet var")) +
                ". Siparişin tamamı iptal edildi — hiçbir ürün rezerve edilmedi.");

        var orderId = placement.OrderId;

        var result = ToolResult.Ok(
            message: $"Sipariş başarıyla oluşturuldu! Sipariş numarası: {orderId}. Ürünler: {FormatLines(merged)}",
            data: new
            {
                orderId,
                lines = ToLineData(merged),
                totalQuantity = merged.Sum(l => l.Quantity),
                customerId,
                status = WellKnown.OrderStatuses.Processing
            });

        _idempotency.Record(WellKnown.ToolNames.OrderPlacement, signature, result, orderId);
        return result;
    }

    /// <summary>Satırların insan-okunur özeti: <c>"Kahve x2, Çay x1"</c>.</summary>
    private static string FormatLines(IReadOnlyList<OrderLine> lines) =>
        string.Join(", ", lines.Select(l => $"{l.Product} x{l.Quantity}"));

    /// <summary>Satırların <c>ToolResult.Data</c> içinde taşınan makine-okunur biçimi.</summary>
    private static object ToLineData(IReadOnlyList<OrderLine> lines) =>
        lines.Select(l => new { product = l.Product, quantity = l.Quantity }).ToList();

    /// <summary>
    /// Sipariş "yok" ile "var ama başkasının" ayrımını KULLANICIYA sızdırmayan ortak mesaj.
    ///
    /// <para>
    /// İki durum farklı metinlerle bildirilirse ortaya bir enumeration oracle'ı çıkar: sipariş
    /// numaraları 4 haneli ve ardışık olduğu için (seed: 1030–1081) saldırgan tarama yapıp
    /// "bulunamadı" = yok, "size ait değil" = VAR ama başkasının çıkarımını yapabilir. Hata
    /// KODU (<see cref="WellKnown.ToolErrorCodes.CustomerIdMismatch"/>) operasyonel görünürlük
    /// için korunur — trace/admin panelinde gerçek sebep görünür; kullanıcıya giden metin aynıdır.
    /// </para>
    /// </summary>
    private static string OrderNotAccessibleMessage(string orderId) =>
        $"'{orderId}' numaralı sipariş bulunamadı.";

    /// <inheritdoc />
    public ToolResult? ValidateOrderActionable(string orderId, string customerId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, OrderNotAccessibleMessage(orderId));

        // Sahiplik ihlali "bulunamadı" ile AYNI metni döner — bkz. OrderNotAccessibleMessage.
        if (!string.Equals(order.CustomerId, customerId, StringComparison.Ordinal))
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.CustomerIdMismatch,
                OrderNotAccessibleMessage(orderId));

        return null;
    }

    [Description("Sipariş durumunu sipariş numarasıyla sorgular. Sonuç ToolResult olarak döner.")]
    public ToolResult OrderStatusTool(
        [Description("Sorgulanacak sipariş numarası (örn: 1030)")] string orderId,
        string customerId)
    {
        if (ValidateOrderActionable(orderId, customerId) is { } blocked) return blocked;

        var order = _orders.Get(orderId)!;

        return ToolResult.Ok(
            message: $"Sipariş No: {orderId}, Ürünler: {order.LinesSummary()}, Durum: {order.Status}.",
            data: new
            {
                orderId,
                lines = ToLineData(order.Lines),
                totalQuantity = order.TotalQuantity(),
                status = order.Status,
                customerId = order.CustomerId,
                orderDate = order.OrderDate
            });
    }

    [Description("Müşterinin en son oluşturduğu siparişi döndürür. Sonuç ToolResult olarak döner.")]
    public ToolResult GetLastOrderTool(
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return ToolResult.ValidationError("Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);

        var result = _orders.GetLast(customerId);
        if (result is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.NoOrdersForCustomer, $"Henüz {customerId} kimlik numarası ile kayıtlı bir siparişiniz bulunmamaktadır.");

        var (orderId, order) = result.Value;
        return ToolResult.Ok(
            message: $"Sipariş No: {orderId}, Ürünler: {order.LinesSummary()}, Durum: {order.Status}, Tarih: {order.OrderDate:g}",
            data: new
            {
                orderId,
                lines = ToLineData(order.Lines),
                totalQuantity = order.TotalQuantity(),
                status = order.Status,
                orderDate = order.OrderDate,
                customerId = order.CustomerId
            });
    }

    [Description("Müşterinin tüm siparişlerini listeler. Sonuç ToolResult olarak döner.")]
    public ToolResult GetAllOrdersTool(
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return ToolResult.ValidationError("Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);

        var orders = _orders.GetByCustomer(customerId);
        if (orders.Count == 0)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.NoOrdersForCustomer, $"Henüz {customerId} kimlik numarası ile kayıtlı bir siparişiniz bulunmamaktadır.");

        var listing = orders.Select((o, i) =>
            $"{i + 1}. Sipariş No: {o.OrderId}, Ürünler: {o.Order.LinesSummary()}, Durum: {o.Order.Status}, Tarih: {o.Order.OrderDate:g}");

        return ToolResult.Ok(
            message: string.Join("\n", listing),
            data: new
            {
                totalCount = orders.Count,
                customerId,
                orders = orders.Select(o => new
                {
                    orderId = o.OrderId,
                    lines = ToLineData(o.Order.Lines),
                    totalQuantity = o.Order.TotalQuantity(),
                    status = o.Order.Status,
                    orderDate = o.Order.OrderDate
                }).ToList()
            });
    }

    [Description("Mevcut bir siparişi iptal eder. Sadece 'İşleniyor' veya 'Kargolandı' durumundaki " +
                 "siparişler iptal edilebilir. Sonuç ToolResult olarak döner.")]
    public ToolResult OrderCancelTool(
        [Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')")] string orderId,
        [Description("İptal sebebi (zorunlu, en az 5 karakter)")] string reason,
        string customerId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);
        // Trim: boşluk dolgusuyla ("a    ") min-uzunluk kuralı aşılamamalı.
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return ToolResult.ValidationError("İptal sebebi en az 5 karakter olmalıdır.", WellKnown.ToolParameterNames.Reason);

        // Varlık + sahiplik: ApprovalGateService bunu onay kaydından ÖNCE de çalıştırır;
        // burada tekrar edilir çünkü durum iki an arasında değişmiş olabilir.
        if (ValidateOrderActionable(orderId, customerId) is { } blocked) return blocked;

        var order = _orders.Get(orderId)!;

        if (order.Status == WellKnown.OrderStatuses.Cancelled)
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.OrderAlreadyCancelled,
                $"'{orderId}' numaralı sipariş zaten iptal edilmiş.");

        if (!_orders.Cancel(orderId, reason))
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.OrderNotCancellable,
                $"'{orderId}' numaralı sipariş şu anki durumunda ({order.Status}) iptal edilemez. " +
                "Sadece 'İşleniyor' veya 'Kargolandı' durumundaki siparişler iptal edilebilir.");

        return ToolResult.Ok(
            message: $"Sipariş {orderId} başarıyla iptal edildi.",
            data: new { orderId, previousStatus = order.Status, newStatus = WellKnown.OrderStatuses.Cancelled, reason });
    }

    [Description("Teslim edilmiş bir sipariş için iade talebi oluşturur. Sadece 'Teslim Edildi' durumundaki " +
                 "ve 14 gün içindeki siparişler iade edilebilir. Sonuç ToolResult olarak döner.")]
    public ToolResult ReturnRequestTool(
        [Description("İade talep edilecek sipariş numarası (zorunlu, ör. '1042')")] string orderId,
        [Description("İade sebebi (zorunlu, en az 5 karakter)")] string reason,
        string customerId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);
        // Trim: boşluk dolgusuyla ("a    ") min-uzunluk kuralı aşılamamalı.
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return ToolResult.ValidationError("İade sebebi en az 5 karakter olmalıdır.", WellKnown.ToolParameterNames.Reason);

        // Varlık + sahiplik: ApprovalGateService bunu onay kaydından ÖNCE de çalıştırır;
        // burada tekrar edilir çünkü durum iki an arasında değişmiş olabilir.
        if (ValidateOrderActionable(orderId, customerId) is { } blocked) return blocked;

        var order = _orders.Get(orderId)!;

        if (order.Status == WellKnown.OrderStatuses.ReturnRequested ||
            order.Status == WellKnown.OrderStatuses.ReturnApproved)
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.ReturnAlreadyRequested,
                $"'{orderId}' numaralı sipariş için zaten bir iade talebi mevcut.");

        if (!_orders.RequestReturn(orderId, reason))
        {
            var detail = order.Status != WellKnown.OrderStatuses.Delivered
                ? $"Sipariş durumu '{order.Status}' — sadece 'Teslim Edildi' durumundaki siparişler iade edilebilir."
                : "14 günlük iade süresi dolmuş olabilir.";
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.ReturnNotEligible,
                $"'{orderId}' numaralı sipariş iade edilemez. {detail}");
        }

        return ToolResult.Ok(
            message: $"Sipariş {orderId} için iade talebi başarıyla oluşturuldu. Ücret iadesi 5–7 iş günü içinde yapılacaktır.",
            data: new
            {
                orderId,
                lines = ToLineData(order.Lines),
                totalQuantity = order.TotalQuantity(),
                reason,
                newStatus = WellKnown.OrderStatuses.ReturnRequested
            });
    }
}
