// Ports/Inbound/IOrderFulfillmentPort.cs
// Sipariş durum güncellemesi (depo/fulfillment): kargolandı, teslim edildi. Müşteriye proaktif bildirim.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public enum OrderFulfillmentStatus { Updated, Unchanged, NotFound, InvalidTransition, Invalid }

/// <param name="Order">Güncel sipariş (bulunamadıysa ya da girdi geçersizse null).</param>
public sealed record OrderFulfillmentResult(OrderFulfillmentStatus Status, string OrderId, OrderInfo? Order, string? Error = null);

public interface IOrderFulfillmentPort
{
    OrderInfo? Get(string orderId);

    /// <summary><c>İşleniyor → Kargolandı</c>; geçiş olduysa müşteriye e-posta.</summary>
    Task<OrderFulfillmentResult> MarkShippedAsync(string orderId, string? carrier, string? trackingNumber, CancellationToken ct = default);

    /// <summary><c>Kargolandı → Teslim Edildi</c>; geçiş olduysa müşteriye e-posta.</summary>
    Task<OrderFulfillmentResult> MarkDeliveredAsync(string orderId, CancellationToken ct = default);
}
