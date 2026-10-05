// Application/Services/Notifications/OrderStatusEmailService.cs
// Proaktif bildirim: sipariş kargoya verildi / teslim edildi e-postası.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// Sipariş durum e-postası. Onay sonucu e-postasıyla aynı güvenceler (bkz. <see cref="ApprovalResultEmailService"/>):
/// <c>order-status:{sipariş}:{tür}</c> anahtarı <see cref="INotificationLedger"/>'da talep edilir — aynı geçiş iki
/// pod'da ya da tekrarlanan depo çağrısında işlense de tek e-posta gider. Gönderim başarısızsa talep geri bırakılır;
/// hata durum güncellemesine yansımaz.
/// </summary>
public sealed class OrderStatusEmailService(
    IEmailSender sender,
    INotificationLedger ledger,
    ICustomerRepository customers,
    IOptions<EmailOptions> options,
    ILogger<OrderStatusEmailService> logger)
{
    public const string Shipped = "shipped";
    public const string Delivered = "delivered";

    private readonly EmailOptions _options = options.Value;

    public async Task NotifyAsync(string orderId, OrderInfo order, string kind, CancellationToken ct = default)
    {
        var enabled = kind switch
        {
            Shipped => _options.Notifications.OrderShipped,
            Delivered => _options.Notifications.OrderDelivered,
            _ => false
        };
        if (!_options.Enabled || !enabled) return;
        if (!long.TryParse(order.CustomerId, out var customerId)) return;

        var key = $"order-status:{orderId}:{kind}";
        if (!await ledger.TryClaimAsync(key, ct)) return;

        try
        {
            var email = await customers.GetEmailAsync(customerId, ct);
            if (string.IsNullOrWhiteSpace(email))
            {
                // Talep tutulur: adres yokken yeniden denemenin anlamı yok.
                logger.LogInformation("[Email] Sipariş bildirimi gönderilmedi — müşterinin e-posta adresi yok | order={OrderId}", orderId);
                return;
            }

            var name = await customers.GetFullNameAsync(customerId, ct);
            await sender.SendAsync(OrderStatusEmailComposer.Compose(orderId, order, kind, email, name, _options.PublicBaseUrl), ct);
            logger.LogInformation("[Email] Sipariş bildirimi gönderildi | order={OrderId} kind={Kind} to={To}",
                orderId, kind, PiiMasker.MaskEmail(email));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Email] Sipariş bildirimi gönderilemedi | order={OrderId} kind={Kind}", orderId, kind);
            try { await ledger.ReleaseAsync(key, CancellationToken.None); }
            catch (Exception releaseEx) { logger.LogWarning(releaseEx, "[Email] Defter kaydı geri bırakılamadı | key={Key}", key); }
        }
    }
}
