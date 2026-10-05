// Application/Services/Notifications/ApprovalResultEmailService.cs
// Onay sonucunu müşteriye e-postayla bildirir (uygulama içi bildirime ek olarak).

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// <see cref="IApprovalQueue.RequestDecided"/> olayını işler (barındırma: Api'deki
/// <c>ApprovalEmailNotificationService</c>).
///
/// <para>
/// <b>Bir kez gönderim:</b> olay karar veren pod'da ve pub/sub ile DİĞER pod'larda da tetiklenir. Gönderimden
/// önce <c>approval-result:{id}</c> anahtarı <see cref="INotificationLedger"/>'da talep edilir; yalnızca
/// kazanan pod gönderir. Gönderim başarısızsa talep geri bırakılır.
/// </para>
///
/// <para>
/// <b>Yalnızca sonuçlanmış kayıt:</b> onaylanmış ama yürütmesi sürmekte olan kayıt atlanır — yürütme bitince
/// olay yeniden tetiklenir.
/// </para>
/// </summary>
public sealed class ApprovalResultEmailService
{
    private readonly IEmailSender _sender;
    private readonly INotificationLedger _ledger;
    private readonly ICustomerRepository _customers;
    private readonly EmailOptions _options;
    private readonly ILogger<ApprovalResultEmailService> _logger;

    public ApprovalResultEmailService(
        IEmailSender sender,
        INotificationLedger ledger,
        ICustomerRepository customers,
        IOptions<EmailOptions> options,
        ILogger<ApprovalResultEmailService> logger)
    {
        _sender = sender;
        _ledger = ledger;
        _customers = customers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleDecidedAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled || !_options.Notifications.ApprovalResults) return;
        if (!IsFinal(request)) return;
        if (!long.TryParse(request.CustomerId, out var customerId)) return;

        var key = $"approval-result:{request.Id}";
        if (!await _ledger.TryClaimAsync(key, ct)) return;

        try
        {
            var email = await _customers.GetEmailAsync(customerId, ct);
            if (string.IsNullOrWhiteSpace(email))
            {
                // Talep tutulur: adres yokken yeniden denemenin anlamı yok.
                _logger.LogInformation("[Email] Onay sonucu gönderilmedi — müşterinin e-posta adresi yok | approval={Id}", request.Id);
                return;
            }

            var name = await _customers.GetFullNameAsync(customerId, ct);
            await _sender.SendAsync(ApprovalEmailComposer.Compose(request, email, name, _options.PublicBaseUrl), ct);
            _logger.LogInformation("[Email] Onay sonucu gönderildi | approval={Id} to={To}",
                request.Id, PiiMasker.MaskEmail(email));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Email] Onay sonucu gönderilemedi | approval={Id}", request.Id);
            try { await _ledger.ReleaseAsync(key, CancellationToken.None); }
            catch (Exception releaseEx) { _logger.LogWarning(releaseEx, "[Email] Defter kaydı geri bırakılamadı | key={Key}", key); }
        }
    }

    private static bool IsFinal(ApprovalRequest r) => r.Status switch
    {
        ApprovalStatus.Rejected or ApprovalStatus.Expired => true,
        ApprovalStatus.Approved => r.ExecutionStatus is ApprovalExecutionStatus.Succeeded or ApprovalExecutionStatus.Failed,
        _ => false
    };
}
