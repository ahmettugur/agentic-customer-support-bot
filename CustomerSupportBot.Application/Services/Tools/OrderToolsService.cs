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

    [Description("Yeni sipariş oluşturur. Ürün adı, adet ve müşteri kimlik numarası zorunludur. " +
                 "Sonuç ToolResult olarak döner.")]
    public ToolResult OrderPlacementTool(
        [Description("Sipariş verilecek ürünün adı")] string productName,
        [Description("Sipariş adedi")] int? quantity,
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(customerId)) missing.Add(WellKnown.ToolParameterNames.CustomerId);
        if (string.IsNullOrWhiteSpace(productName)) missing.Add(WellKnown.ToolParameterNames.ProductName);
        if (quantity is null or <= 0) missing.Add(WellKnown.ToolParameterNames.Quantity);
        if (missing.Count > 0)
            return ToolResult.ValidationError(
                $"Sipariş için şu bilgiler gerekli: {string.Join(", ", missing)}.",
                missing.ToArray());

        if (!long.TryParse(customerId, out var customerIdLong)
            || !_customers.Exists(customerIdLong))
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.CustomerNotFound,
                $"'{customerId}' kimlik numaralı müşteri sistemde kayıtlı değil. Lütfen müşteri numaranızı kontrol edin.");

        var product = _products.FindProduct(productName);
        if (product is null)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");

        // Mükerrer çağrı koruması — stok düşülmeden ve sipariş yazılmadan ÖNCE.
        // İmza kanonik ürün adı üzerinden kurulur, böylece "kahve"/"Kahve" aynı sayılır.
        var signature = new object?[] { product.Name, quantity!.Value, customerId };
        if (_idempotency.TryGetRecent(WellKnown.ToolNames.OrderPlacement, signature, out var recent))
        {
            return ToolResult.Ok(
                message: $"Bu siparişi az önce oluşturmuştum — sipariş numarası: {recent.EntityId}. " +
                         "Mükerrer kayıt oluşturmadım. Gerçekten ikinci bir sipariş istiyorsanız lütfen açıkça belirtin.",
                data: new
                {
                    orderId = recent.EntityId,
                    product = product.Name,
                    quantity = quantity.Value,
                    customerId,
                    status = WellKnown.OrderStatuses.Processing,
                    duplicate = true
                },
                confidence: 0.9);
        }

        if (!_products.TryDeductStock(product.Name, quantity.Value))
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.StockInsufficient,
                $"Yalnızca {product.Stock} adet {product.Name} stokta mevcut, {quantity.Value} adet sipariş verilemez.");

        var orderId = _orders.Create(new OrderInfo
        {
            Product    = product.Name,
            Quantity   = quantity.Value,
            CustomerId = customerId,
            Status     = WellKnown.OrderStatuses.Processing,
            OrderDate  = DateTime.Now
        });

        var result = ToolResult.Ok(
            message: $"Sipariş başarıyla oluşturuldu! Sipariş numarası: {orderId}",
            data: new { orderId, product = product.Name, quantity = quantity.Value, customerId, status = WellKnown.OrderStatuses.Processing });

        _idempotency.Record(WellKnown.ToolNames.OrderPlacement, signature, result, orderId);
        return result;
    }

    [Description("Sipariş durumunu sipariş numarasıyla sorgular. Sonuç ToolResult olarak döner.")]
    public ToolResult OrderStatusTool(
        [Description("Sorgulanacak sipariş numarası (örn: 1030)")] string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, $"'{orderId}' numaralı sipariş bulunamadı.");

        return ToolResult.Ok(
            message: $"Sipariş No: {orderId}, Ürün: {order.Product}, Adet: {order.Quantity}, Durum: {order.Status}.",
            data: new { orderId, product = order.Product, quantity = order.Quantity, status = order.Status, customerId = order.CustomerId, orderDate = order.OrderDate });
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
            message: $"Sipariş No: {orderId}, Ürün: {order.Product}, Adet: {order.Quantity}, Durum: {order.Status}, Tarih: {order.OrderDate:g}",
            data: new { orderId, product = order.Product, quantity = order.Quantity, status = order.Status, orderDate = order.OrderDate, customerId = order.CustomerId });
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

        var lines = orders.Select((o, i) =>
            $"{i + 1}. Sipariş No: {o.OrderId}, Ürün: {o.Order.Product}, Adet: {o.Order.Quantity}, Durum: {o.Order.Status}, Tarih: {o.Order.OrderDate:g}");

        return ToolResult.Ok(
            message: string.Join("\n", lines),
            data: new
            {
                totalCount = orders.Count,
                customerId,
                orders = orders.Select(o => new
                {
                    orderId = o.OrderId, product = o.Order.Product, quantity = o.Order.Quantity,
                    status = o.Order.Status, orderDate = o.Order.OrderDate
                }).ToList()
            });
    }

    [Description("Mevcut bir siparişi iptal eder. Sadece 'İşleniyor' veya 'Kargolandı' durumundaki " +
                 "siparişler iptal edilebilir. Sonuç ToolResult olarak döner.")]
    public ToolResult OrderCancelTool(
        [Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')")] string orderId,
        [Description("İptal sebebi (zorunlu, en az 5 karakter)")] string reason)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);
        // Trim: boşluk dolgusuyla ("a    ") min-uzunluk kuralı aşılamamalı.
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return ToolResult.ValidationError("İptal sebebi en az 5 karakter olmalıdır.", WellKnown.ToolParameterNames.Reason);

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, $"'{orderId}' numaralı sipariş bulunamadı.");

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
        [Description("İade sebebi (zorunlu, en az 5 karakter)")] string reason)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return ToolResult.ValidationError("Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);
        // Trim: boşluk dolgusuyla ("a    ") min-uzunluk kuralı aşılamamalı.
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return ToolResult.ValidationError("İade sebebi en az 5 karakter olmalıdır.", WellKnown.ToolParameterNames.Reason);

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, $"'{orderId}' numaralı sipariş bulunamadı.");

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
            data: new { orderId, product = order.Product, quantity = order.Quantity, reason, newStatus = WellKnown.OrderStatuses.ReturnRequested });
    }
}
