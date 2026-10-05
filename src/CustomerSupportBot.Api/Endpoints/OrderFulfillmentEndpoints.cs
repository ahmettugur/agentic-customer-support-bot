// Endpoints/OrderFulfillmentEndpoints.cs
// Sipariş durum güncellemesi (depo/fulfillment sistemi ya da yönetici). Geçişte müşteriye e-posta gider.
//   GET  /orders/{orderId}             [Admin]  — panel: siparişi bul
//   POST /orders/{orderId}/shipment    [Admin]  — { carrier?, trackingNumber? }  İşleniyor → Kargolandı
//   POST /orders/{orderId}/delivery    [Admin]  —                               Kargolandı → Teslim Edildi
// 404 bulunamadı, 409 geçersiz geçiş (currentStatus ile), 400 geçersiz girdi; aynı duruma tekrar geçiş 200 + changed=false.
// Yetki ve hız sınırı çağıran grupta (Program.cs: adminScope).

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class OrderFulfillmentEndpoints
{
    public sealed record ShipmentInput(string? Carrier, string? TrackingNumber);

    public static IEndpointRouteBuilder MapOrderFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{orderId}", (string orderId, IOrderFulfillmentPort orders) =>
            orders.Get(orderId) is { } order ? Results.Ok(View(orderId, order)) : Results.NotFound());

        app.MapPost("/orders/{orderId}/shipment",
            async (string orderId, ShipmentInput? body, IOrderFulfillmentPort orders, CancellationToken ct) =>
                ToResult(await orders.MarkShippedAsync(orderId, body?.Carrier, body?.TrackingNumber, ct)));

        app.MapPost("/orders/{orderId}/delivery",
            async (string orderId, IOrderFulfillmentPort orders, CancellationToken ct) =>
                ToResult(await orders.MarkDeliveredAsync(orderId, ct)));

        return app;
    }

    private static IResult ToResult(OrderFulfillmentResult r) => r.Status switch
    {
        OrderFulfillmentStatus.NotFound => Results.NotFound(new { error = "Sipariş bulunamadı." }),
        OrderFulfillmentStatus.Invalid => Results.BadRequest(new { error = r.Error }),
        OrderFulfillmentStatus.InvalidTransition => Results.Conflict(new
        {
            error = "Sipariş bu durumdan güncellenemez.",
            currentStatus = r.Order?.Status
        }),
        _ => Results.Ok(new
        {
            orderId = r.OrderId,
            changed = r.Status == OrderFulfillmentStatus.Updated,
            order = r.Order is null ? null : View(r.OrderId, r.Order)
        })
    };

    private static object View(string orderId, OrderInfo o) => new
    {
        orderId,
        customerId = o.CustomerId,
        status = o.Status,
        linesSummary = o.LinesSummary(),
        orderDate = o.OrderDate,
        shippedAt = o.ShippedAt,
        carrier = o.Carrier,
        trackingNumber = o.TrackingNumber,
        deliveredAt = o.DeliveredAt
    };
}
