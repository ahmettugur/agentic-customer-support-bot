// Application/Services/Notifications/CustomerNotification.cs
// Sohbet içi (anlık) müşteri bildirimi: açık sohbet ekranına toast / sistem notu olarak iner.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// Sohbet içi bildirim. Kalıcı kanal e-postadır (bkz. <see cref="OrderStatusEmailService"/>); bu bildirim
/// yalnızca müşterinin o an açık bir sohbet akışı varsa görür — kaçırılırsa yeniden gönderilmez.
/// </summary>
/// <param name="Kind"><see cref="CustomerNotificationKinds"/> değerlerinden biri.</param>
/// <param name="ReferenceId">Sipariş numarası ya da şikayet kimliği.</param>
public sealed record CustomerNotification(string Kind, string Title, string Text, string? ReferenceId, DateTime CreatedAt);

public static class CustomerNotificationKinds
{
    public const string OrderShipped = "order_shipped";
    public const string OrderDelivered = "order_delivered";
    public const string ComplaintInReview = "complaint_in_review";
    public const string ComplaintClosed = "complaint_closed";
}

/// <summary>Bildirim metinleri (saf). Bilinmeyen tür için null döner — çağıran hiçbir şey yayınlamaz.</summary>
public static class CustomerNotifications
{
    public static CustomerNotification? ForOrder(string orderId, OrderInfo order, string kind, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        switch (kind)
        {
            case OrderStatusEmailService.Shipped:
                var detail = string.Join(" ", new[]
                {
                    string.IsNullOrWhiteSpace(order.Carrier) ? null : $"{order.Carrier.Trim()} ile yola çıktı.",
                    string.IsNullOrWhiteSpace(order.TrackingNumber) ? null : $"Takip no: {order.TrackingNumber.Trim()}"
                }.Where(x => x is not null));
                return new(CustomerNotificationKinds.OrderShipped, "Siparişiniz kargoya verildi",
                    $"#{orderId} numaralı siparişiniz kargoya verildi. {detail}".Trim(), orderId, at);
            case OrderStatusEmailService.Delivered:
                return new(CustomerNotificationKinds.OrderDelivered, "Siparişiniz teslim edildi",
                    $"#{orderId} numaralı siparişiniz teslim edildi.", orderId, at);
            default:
                return null;
        }
    }

    public static CustomerNotification? ForComplaint(string complaintId, ComplaintInfo complaint, string kind, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        return kind switch
        {
            ComplaintStatusEmailService.InReview => new(CustomerNotificationKinds.ComplaintInReview,
                "Şikayetiniz inceleniyor", $"#{complaint.OrderId} numaralı siparişle ilgili şikayetiniz incelemeye alındı.",
                complaintId, at),
            ComplaintStatusEmailService.Closed => new(CustomerNotificationKinds.ComplaintClosed,
                "Şikayetiniz sonuçlandı", $"#{complaint.OrderId} numaralı siparişle ilgili şikayetiniz sonuçlandı. Ayrıntılar e-postanızda.",
                complaintId, at),
            _ => null
        };
    }
}
