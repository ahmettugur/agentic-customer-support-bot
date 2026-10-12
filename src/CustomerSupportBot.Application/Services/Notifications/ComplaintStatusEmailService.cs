// Application/Services/Notifications/ComplaintStatusEmailService.cs
// Şikayet incelemede / kapandı e-postası — OrderStatusEmailService ile aynı güvenceler.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// Anahtar <c>complaint:{no}:{tür}:{sürüm}</c> — sürüm her geçişte arttığı için yeniden açılıp tekrar kapanan
/// şikayet yeni e-posta alır, aynı geçişin iki pod'da işlenmesi ise tek e-posta gönderir. Gönderim
/// başarısızsa talep geri bırakılır; hata geçişi geri almaz.
/// </summary>
public sealed class ComplaintStatusEmailService(
    IEmailSender sender,
    INotificationLedger ledger,
    ICustomerRepository customers,
    IOptions<EmailOptions> options,
    ILogger<ComplaintStatusEmailService> logger)
{
    public const string InReview = "in_review";
    public const string Closed = "closed";

    private readonly EmailOptions _options = options.Value;

    public async Task NotifyAsync(string complaintId, ComplaintInfo complaint, string kind, CancellationToken ct = default)
    {
        var enabled = kind switch
        {
            InReview => _options.Notifications.ComplaintInReview,
            Closed => _options.Notifications.ComplaintClosed,
            _ => false
        };
        if (!_options.Enabled || !enabled) return;
        if (!long.TryParse(complaint.CustomerId, out var customerId)) return;

        var key = $"complaint:{complaintId}:{kind}:{complaint.Version}";
        if (!await ledger.TryClaimAsync(key, ct)) return;

        try
        {
            var email = await customers.GetEmailAsync(customerId, ct);
            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogInformation("[Email] Şikayet bildirimi gönderilmedi — müşterinin e-posta adresi yok | complaint={ComplaintId}", complaintId);
                return;
            }

            var name = await customers.GetFullNameAsync(customerId, ct);
            await sender.SendAsync(ComplaintStatusEmailComposer.Compose(complaintId, complaint, kind, email, name, _options.PublicBaseUrl), ct);
            logger.LogInformation("[Email] Şikayet bildirimi gönderildi | complaint={ComplaintId} kind={Kind} to={To}",
                complaintId, kind, PiiMasker.MaskEmail(email));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Email] Şikayet bildirimi gönderilemedi | complaint={ComplaintId} kind={Kind}", complaintId, kind);
            try { await ledger.ReleaseAsync(key, CancellationToken.None); }
            catch (Exception releaseEx) { logger.LogWarning(releaseEx, "[Email] Defter kaydı geri bırakılamadı | key={Key}", key); }
        }
    }
}
