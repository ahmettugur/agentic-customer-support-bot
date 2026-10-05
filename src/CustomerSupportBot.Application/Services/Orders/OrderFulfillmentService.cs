// Application/Services/Orders/OrderFulfillmentService.cs
// Sipariş durum geçişleri + proaktif bildirim.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Orders;

/// <summary>
/// <see cref="IOrderFulfillmentPort"/> uygulaması. Bildirim yalnızca gerçek geçişte (<see cref="OrderStatusChange.Updated"/>)
/// gider — tekrarlanan çağrı (depo sistemi aynı olayı yeniden gönderdi) müşteriye ikinci e-posta göndermez.
/// Bildirim hatası durum güncellemesini geri almaz (bkz. <see cref="OrderStatusEmailService"/>).
/// </summary>
public sealed class OrderFulfillmentService(IOrderRepository orders, OrderStatusEmailService notifier, TimeProvider? clock = null)
    : IOrderFulfillmentPort
{
    public const int MaxCarrierLength = 64;
    public const int MaxTrackingLength = 64;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public OrderInfo? Get(string orderId) => orders.Get(orderId);

    public async Task<OrderFulfillmentResult> MarkShippedAsync(
        string orderId, string? carrier, string? trackingNumber, CancellationToken ct = default)
    {
        carrier = Blank(carrier);
        trackingNumber = Blank(trackingNumber);
        if (carrier is { Length: > MaxCarrierLength })
            return new OrderFulfillmentResult(OrderFulfillmentStatus.Invalid, orderId, null,
                $"Kargo firması en fazla {MaxCarrierLength} karakter olabilir.");
        if (trackingNumber is { Length: > MaxTrackingLength })
            return new OrderFulfillmentResult(OrderFulfillmentStatus.Invalid, orderId, null,
                $"Takip numarası en fazla {MaxTrackingLength} karakter olabilir.");

        var result = orders.MarkShipped(orderId, carrier, trackingNumber, _clock.GetUtcNow().UtcDateTime);
        return await CompleteAsync(orderId, result, OrderStatusEmailService.Shipped, ct);
    }

    public async Task<OrderFulfillmentResult> MarkDeliveredAsync(string orderId, CancellationToken ct = default)
    {
        var result = orders.MarkDelivered(orderId, _clock.GetUtcNow().UtcDateTime);
        return await CompleteAsync(orderId, result, OrderStatusEmailService.Delivered, ct);
    }

    private async Task<OrderFulfillmentResult> CompleteAsync(
        string orderId, OrderStatusUpdateResult result, string kind, CancellationToken ct)
    {
        if (result.Change == OrderStatusChange.Updated && result.Order is not null)
            await notifier.NotifyAsync(orderId, result.Order, kind, ct);

        var status = result.Change switch
        {
            OrderStatusChange.Updated => OrderFulfillmentStatus.Updated,
            OrderStatusChange.Unchanged => OrderFulfillmentStatus.Unchanged,
            OrderStatusChange.NotFound => OrderFulfillmentStatus.NotFound,
            _ => OrderFulfillmentStatus.InvalidTransition
        };
        return new OrderFulfillmentResult(status, orderId, result.Order);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
