using System.Collections.Concurrent;

namespace CustomerSupportBot.Models;

/// <summary>
/// Thread-safe erişim için ConcurrentDictionary kullanılır.
/// Uygulama yeniden başlatılana kadar veriler bellekte kalır.
/// </summary>
public static class FakeDatabase
{
    // ─── ÜRÜN KATALOĞU ───
    // PRODUCT_CATALOG = { "Dell XPS 15": {"price": 1500, "stock": 10}, ... }
    public static readonly ConcurrentDictionary<string, ProductInfo> ProductCatalog = new()
    {
        ["Dell XPS 15"] = new ProductInfo(1500, 10),
        ["Apple iPhone 15 Pro"] = new ProductInfo(1200, 15),
        ["Sony WH-1000XM5 Headphones"] = new ProductInfo(350, 20),
        ["Samsung Galaxy Tab S9"] = new ProductInfo(900, 12),
        ["Logitech MX Master 3S Mouse"] = new ProductInfo(100, 30)
    };

    // ─── SİPARİŞ VERİTABANI ───
    // ORDERS_DB = { "ORD-1": {...}, "ORD-2": {...} }
    public static readonly ConcurrentDictionary<string, OrderInfo> OrdersDb = new()
    {
        ["ORD-1"] = new OrderInfo
        {
            Product = "Dell XPS 15",
            Quantity = 1,
            CustomerId = "CUST-1990",
            Status = WellKnown.OrderStatuses.Shipped,
            OrderDate = new DateTime(2024, 1, 15, 10, 30, 0)
        },
        ["ORD-2"] = new OrderInfo
        {
            Product = "Apple iPhone 15 Pro",
            Quantity = 2,
            CustomerId = "CUST-1990",
            Status = WellKnown.OrderStatuses.Processing,
            OrderDate = new DateTime(2024, 2, 20, 14, 45, 0)
        }
    };

    // ─── ŞİKAYET VERİTABANI ───
    // COMPLAINTS_DB = { "CMP-1": {...}, "CMP-2": {...} }
    public static readonly ConcurrentDictionary<string, ComplaintInfo> ComplaintsDb = new()
    {
        ["CMP-1"] = new ComplaintInfo
        {
            OrderId = "ORD-1",
            CustomerId = "CUST-1990",
            Complaint = "Arızalı bir ürün teslim aldım.",
            Status = WellKnown.ComplaintStatuses.Resolved
        },
        ["CMP-2"] = new ComplaintInfo
        {
            OrderId = "ORD-2",
            CustomerId = "CUST-1990",
            Complaint = "Siparişim gecikti.",
            Status = WellKnown.ComplaintStatuses.Pending
        }
    };

    // ─── OTOMATİK ID SAYAÇLARI ───
    // Thread-safe artırım için Interlocked.Increment kullanılır.
    private static int _orderCounter = 2;
    private static int _complaintCounter = 2;

    /// <summary>
    /// Yeni sipariş numarası üretir: ORD-3, ORD-4, ...
    /// </summary>
    public static string GetNextOrderId() =>
        $"ORD-{Interlocked.Increment(ref _orderCounter)}";

    /// <summary>
    /// Yeni şikayet numarası üretir: CMP-3, CMP-4, ...
    /// </summary>
    public static string GetNextComplaintId() =>
        $"CMP-{Interlocked.Increment(ref _complaintCounter)}";

    /// <summary>
    /// Ürün adı ile yakın eşleşme arar (büyük/küçük harf duyarsız alt-string eşleşmesi).
    /// </summary>
    public static string? FindClosestProduct(string productName)
    {
        foreach (var key in ProductCatalog.Keys)
        {
            if (key.Contains(productName, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    /// <summary>
    /// Belirtilen müşterinin en son oluşturduğu siparişi döndürür.
    /// </summary>
    public static (string OrderId, OrderInfo Order)? GetLastOrder(string customerId)
    {
        var entry = OrdersDb
            .Where(kvp => kvp.Value.CustomerId == customerId)
            .OrderByDescending(kvp => kvp.Value.OrderDate)
            .FirstOrDefault();

        return entry.Key != null ? (entry.Key, entry.Value) : null;
    }

    /// <summary>
    /// Belirtilen müşterinin tüm siparişlerini sipariş tarihine göre azalan sırada döndürür.
    /// </summary>
    public static IEnumerable<(string OrderId, OrderInfo Order)> GetAllOrders(string customerId)
    {
        return OrdersDb
            .Where(kvp => kvp.Value.CustomerId == customerId)
            .OrderByDescending(kvp => kvp.Value.OrderDate)
            .Select(kvp => (kvp.Key, kvp.Value));
    }
}