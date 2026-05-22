// Application/Services/CustomerSupportToolsService.cs
// AI tool'larının Application implementasyonu.
//
// Hexagonal dönüşümün kalbi: FakeDatabase statik bağımlılıkları
// IProductCatalogRepository, IOrderRepository, IComplaintRepository
// port'larına dönüştürüldü. Bu sınıf sadece Domain modellerine ve port'lara bağımlıdır;
// hangi adaptörün (InMemory / Postgres) kullanıldığını bilmez.
//
// AIFunctionFactory.Create(svc.ProductInquiryTool) şeklinde kullanılır —
// instance metot, static yerine DI ile çözümlenir.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Müşteri destek AI araçları.
/// Statik FakeDatabase bağımlılığı kırılmış; tüm veri erişimi port'lar üzerinden yapılır.
/// </summary>
public sealed class CustomerSupportToolsService : ICustomerSupportToolsService
{
    private readonly IProductCatalogRepository _products;
    private readonly IOrderRepository _orders;
    private readonly IComplaintRepository _complaints;

    // ─── Idempotency cache ───
    private readonly ConcurrentDictionary<string, (DateTime At, ToolResult Result)> _idempotencyCache = new();
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(60);

    public CustomerSupportToolsService(
        IProductCatalogRepository products,
        IOrderRepository orders,
        IComplaintRepository complaints)
    {
        _products = products;
        _orders = orders;
        _complaints = complaints;
    }

    // ════════════════════════════════════════════════════════════════
    // ÜRÜN SORGULAMA
    // ════════════════════════════════════════════════════════════════

    [Description("Ürün kataloğundan ürün bilgisi sorgular. Ürün adı veya kısmi adı ile arama yapar. " +
                 "Sonuç yapılandırılmış ToolResult olarak döner (success, confidence, data, error).")]
    public ToolResult ProductInquiryTool(
        [Description("Sorgulanacak ürünün adı veya kısmi adı")] string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return ToolResult.ValidationError(
                "Ürün adı boş olamaz. Hangi ürünü aradığınızı belirtin.",
                WellKnown.ToolParameterNames.ProductName);

        var product = _products.FindProduct(productName);
        if (product is null)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");

        var isExact = product.Name.Equals(productName, StringComparison.OrdinalIgnoreCase);
        return ToolResult.Ok(
            message: $"{product.Name}: Fiyat = ${product.Price}, Stok = {product.Stock} adet.",
            data: new { name = product.Name, price = product.Price, stock = product.Stock },
            confidence: isExact ? 1.0 : 0.85);
    }

    // ════════════════════════════════════════════════════════════════
    // SİPARİŞ OLUŞTURMA
    // ════════════════════════════════════════════════════════════════

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

        var product = _products.FindProduct(productName);
        if (product is null)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");

        var idemKey = ComputeKey("order_placement", customerId, product.Name, quantity!.Value.ToString());
        if (TryGetCached(idemKey, out var cached)) return cached;

        if (!_products.TryDeductStock(product.Name, quantity!.Value))
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
        StoreResult(idemKey, result);
        return result;
    }

    // ════════════════════════════════════════════════════════════════
    // SİPARİŞ DURUM SORGULAMA
    // ════════════════════════════════════════════════════════════════

    [Description("Sipariş durumunu sipariş numarasıyla sorgular. Sonuç ToolResult olarak döner.")]
    public ToolResult OrderStatusTool(
        [Description("Sorgulanacak sipariş numarası (örn: ORD-1)")] string orderId)
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

    // ════════════════════════════════════════════════════════════════
    // ŞİKAYET KAYIT
    // ════════════════════════════════════════════════════════════════

    [Description("Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur; " +
                 "customer_id opsiyoneldir (boşsa order_id üzerinden siparişten otomatik türetilir). " +
                 "Sonuç ToolResult olarak döner.")]
    public ToolResult ComplaintRegistrationTool(
        [Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu, ör. 'ORD-1')")] string orderId,
        [Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText,
        [Description("Müşteri kimlik numarası (opsiyonel; boşsa siparişten türetilir)")] string? customerId = null)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(orderId)) missing.Add(WellKnown.ToolParameterNames.OrderId);
        if (string.IsNullOrWhiteSpace(complaintText) || complaintText.Length < 10)
            missing.Add($"{WellKnown.ToolParameterNames.ComplaintDescription} (en az 10 karakter)");
        if (missing.Count > 0)
            return ToolResult.ValidationError($"Şikayet kaydı için şu bilgiler gerekli: {string.Join(", ", missing)}.", missing.ToArray());

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, $"'{orderId}' numaralı sipariş bulunamadı, şikayet kaydı oluşturulamadı.");

        var effectiveCustomerId = customerId;
        var inferred = false;
        if (string.IsNullOrWhiteSpace(effectiveCustomerId))
        {
            effectiveCustomerId = order.CustomerId;
            inferred = true;
        }
        else if (!string.Equals(effectiveCustomerId, order.CustomerId, StringComparison.Ordinal))
        {
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.CustomerIdMismatch,
                $"Sağladığınız müşteri kimliği ({effectiveCustomerId}) '{orderId}' siparişinin sahibiyle eşleşmiyor.");
        }

        var idemKey = ComputeKey("complaint_registration", effectiveCustomerId, orderId, complaintText);
        if (TryGetCached(idemKey, out var cached)) return cached;

        var complaintId = _complaints.Create(new ComplaintInfo
        {
            OrderId    = orderId,
            CustomerId = effectiveCustomerId!,
            Complaint  = complaintText,
            Status     = WellKnown.ComplaintStatuses.Pending
        });

        var result = ToolResult.Ok(
            message: $"Şikayet başarıyla kaydedildi! Şikayet numarası: {complaintId}",
            data: new { complaintId, orderId, customerId = effectiveCustomerId, customerIdInferred = inferred, status = WellKnown.ComplaintStatuses.Pending });
        StoreResult(idemKey, result);
        return result;
    }

    // ════════════════════════════════════════════════════════════════
    // SİPARİŞ LİSTELEME
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    // İNSAN TEMSİLCİ HANDOFF (yan etkisiz)
    // ════════════════════════════════════════════════════════════════

    [Description("Kullanıcıyı bir müşteri temsilcisine yönlendirme talebini kaydeder. " +
                 "Tool yan etkisi yoktur — yalnızca niyeti formalize eder.")]
    public static ToolResult HumanHandoffTool(
        [Description("Kullanıcının temsilciyle görüşme isteme sebebi (1-2 cümle).")] string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ToolResult.ValidationError("Handoff talebi için sebep alanı doldurulmalı.", WellKnown.ToolParameterNames.Reason);

        return ToolResult.Ok(
            message: "Temsilci yönlendirme talebiniz alındı. Kısa süre içinde bir temsilci sizinle iletişime geçecek.",
            data: new { handoffRequested = true, reason = reason.Trim() },
            confidence: 1.0);
    }

    // ════════════════════════════════════════════════════════════════
    // Idempotency yardımcıları
    // ════════════════════════════════════════════════════════════════

    private static string ComputeKey(string toolName, params string?[] parts)
    {
        var raw = string.Join("|", parts.Select(p => p?.Trim().ToLowerInvariant() ?? ""));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"{toolName}:{Convert.ToHexString(bytes)[..16]}";
    }

    private bool TryGetCached(string key, out ToolResult cached)
    {
        if (_idempotencyCache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.At < IdempotencyWindow)
        {
            cached = entry.Result;
            return true;
        }
        cached = default!;
        return false;
    }

    private void StoreResult(string key, ToolResult result)
    {
        _idempotencyCache[key] = (DateTime.UtcNow, result);
        if (_idempotencyCache.Count > 200)
        {
            var cutoff = DateTime.UtcNow - IdempotencyWindow;
            foreach (var stale in _idempotencyCache.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList())
                _idempotencyCache.TryRemove(stale, out _);
        }
    }
}
