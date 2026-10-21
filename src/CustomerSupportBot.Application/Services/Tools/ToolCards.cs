// Application/Services/Tools/ToolCards.cs
// Sohbet ekranının çizdiği YAPILANDIRILMIŞ kartlar (ui_hint). Araç sonucunun metni modelin diline girer; kart ise veritabanındaki
// gerçek veriden gelir ve müşteriye görünmeyecek hiçbir alanı (iç not, temsilci, onay kaydı) taşımaz.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

internal static class ToolCards
{
    /// <summary>"Siparişlerim" kartı sayısı: tüm liste metinde verilir, kart yalnızca en yenileri gösterir.</summary>
    internal const int MaxOrderCards = 5;
    internal const int MaxComplaintCards = 5;
    private const int MaxComplaintTextChars = 200;

    private sealed record Line(string Product, int Quantity);

    private sealed record OrderCard(
        string OrderId, string Status, DateTime OrderDate, IReadOnlyList<Line> Lines, int TotalQuantity,
        string? Carrier, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime? CancelledAt);

    private sealed record ComplaintCard(
        string ComplaintId, string OrderId, string Status, string Complaint, string? Resolution, DateTime? UpdatedAt);

    private static OrderCard Order(string orderId, OrderInfo o) => new(
        orderId, o.Status, o.OrderDate, o.Lines.Select(l => new Line(l.Product, l.Quantity)).ToList(), o.TotalQuantity(),
        o.Carrier, o.TrackingNumber, o.ShippedAt, o.DeliveredAt, o.CancelledAt);

    /// <summary>Tek sipariş kartı. Yayıcı yoksa ya da kanal kart gösteremiyorsa sessizce atlanır.</summary>
    internal static void EmitOrder(IUiHintEmitter? hints, string orderId, OrderInfo order) =>
        hints?.Emit(new StreamEvent(StreamEventTypes.UiHint,
            new { kind = "order_cards", totalCount = 1, orders = new[] { Order(orderId, order) } }));

    /// <summary>En yeni <see cref="MaxOrderCards"/> sipariş; <c>totalCount</c> tüm sayıyı taşır.</summary>
    internal static void EmitOrders(IUiHintEmitter? hints, IReadOnlyList<(string OrderId, OrderInfo Order)> orders) =>
        hints?.Emit(new StreamEvent(StreamEventTypes.UiHint, new
        {
            kind = "order_cards",
            totalCount = orders.Count,
            orders = orders.OrderByDescending(o => o.Order.OrderDate).Take(MaxOrderCards).Select(o => Order(o.OrderId, o.Order)).ToList()
        }));

    private static ComplaintCard Complaint(string id, ComplaintInfo c) => new(
        id, c.OrderId, c.Status,
        c.Complaint.Length <= MaxComplaintTextChars ? c.Complaint : c.Complaint[..MaxComplaintTextChars] + "…",
        // Açıklama yalnızca KAPANMIŞ şikayette müşteriye gösterilir (ComplaintStatusTool ile aynı kural).
        WellKnown.ComplaintStatuses.IsClosed(c.Status) && !string.IsNullOrWhiteSpace(c.Resolution) ? c.Resolution : null,
        c.UpdatedAt == default ? null : c.UpdatedAt);

    internal static void EmitComplaints(IUiHintEmitter? hints, IEnumerable<(string ComplaintId, ComplaintInfo Complaint)> complaints) =>
        hints?.Emit(new StreamEvent(StreamEventTypes.UiHint, new
        {
            kind = "complaint_cards",
            complaints = complaints.Take(MaxComplaintCards).Select(c => Complaint(c.ComplaintId, c.Complaint)).ToList()
        }));
}
