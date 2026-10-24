// Application/Services/Notifications/ProactiveOutreachService.cs
// Proaktif ajan: müşteri sormadan geciken kargoyu fark edip haber verir.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Notifications;

public sealed class ProactiveOutreachOptions
{
    public const string SectionName = "ProactiveOutreach";

    /// <summary>Müşteriye kendiliğinden yazan her özellik gibi KAPALI başlar; bilinçli açılır.</summary>
    public bool Enabled { get; set; }

    /// <summary>Kargolanalı bu kadar gün geçtiği hâlde teslim edilmemiş sipariş "geciken" sayılır.</summary>
    public int DelayedShipmentDays { get; set; } = 5;

    /// <summary>Açık bir şikayet bu kadar gündür güncellenmediyse müşteriye bir kez haber verilir.</summary>
    public int StaleComplaintDays { get; set; } = 3;

    /// <summary>
    /// Bu süreden ESKİ kayıtlar tarihî sayılır ve bildirilmez. Özelliği ilk açtığınızda yıllar önce kargolanmış/açık kalmış kayıtlara
    /// toplu e-posta gitmesini önler (gerçek bir canlı denemede tek taramada seed şikayetlerine e-posta gitti).
    /// </summary>
    public int MaxAgeDays { get; set; } = 30;

    public int PollIntervalMinutes { get; set; } = 30;

    /// <summary>Bir taramada işlenecek en çok sipariş (ilk açılışta birikmiş yüzlerce siparişe aynı anda e-posta gitmesin).</summary>
    public int BatchSize { get; set; } = 50;
}

/// <summary>
/// Her geciken sipariş için <c>proactive:delayed-shipment:{sipariş}</c> anahtarı <see cref="INotificationLedger"/>'da talep edilir:
/// çok pod'da ve her taramada sipariş başına tek bildirim. Sohbet içi bildirim e-postadan bağımsızdır (e-posta kapalı ya da adres
/// yoksa da gider); e-posta gönderilemezse talep geri bırakılır ve sonraki tarama yeniden dener.
/// </summary>
public sealed class ProactiveOutreachService(
    IOrderRepository orders,
    IEmailSender sender,
    INotificationLedger ledger,
    ICustomerRepository customers,
    CustomerNotificationHub? hub,
    IOptions<ProactiveOutreachOptions> options,
    IOptions<EmailOptions> email,
    ILogger<ProactiveOutreachService> logger,
    TimeProvider? clock = null,
    IComplaintRepository? complaints = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Bir tarama yapar; bu turda bildirilen sipariş sayısını döner.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.Enabled) return 0;

        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-Math.Max(1, o.DelayedShipmentDays));
        var notified = 0;

        foreach (var (orderId, order) in orders.GetShippedBefore(cutoff, Math.Max(1, o.BatchSize), now.AddDays(-Math.Max(1, o.MaxAgeDays))))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!long.TryParse(order.CustomerId, out var customerId)) continue;

                var key = $"proactive:delayed-shipment:{orderId}";
                if (!await ledger.TryClaimAsync(key, ct)) continue;

                var days = Math.Max(1, (int)Math.Floor((now - (order.ShippedAt ?? cutoff)).TotalDays));
                hub?.Publish(order.CustomerId, CustomerNotifications.ForDelayedShipment(orderId, order, days, now));
                notified++;

                if (!email.Value.Enabled || !sender.IsEnabled) continue;
                if (!await SendEmailAsync(key, orderId, order, customerId, days, ct)) notified--;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bir siparişin hatası taramanın geri kalanını durdurmaz.
                logger.LogError(ex, "[Proactive] Geciken kargo bildirimi işlenemedi | order={OrderId}", orderId);
            }
        }

        notified += await NotifyStaleComplaintsAsync(o, now, ct);

        if (notified > 0) logger.LogInformation("[Proactive] {Count} proaktif bildirim gönderildi", notified);
        return notified;
    }

    private async Task<int> NotifyStaleComplaintsAsync(ProactiveOutreachOptions o, DateTime now, CancellationToken ct)
    {
        if (complaints is null) return 0;
        var cutoff = now.AddDays(-Math.Max(1, o.StaleComplaintDays));
        var notified = 0;

        var floor = now.AddDays(-Math.Max(1, o.MaxAgeDays));

        // Açık şikayetler en eskiden başlar: tarihî kayıtlar sığ bir sayfayı doldurup güncelleri aç bırakmasın diye geniş sayfa çekilir,
        // pencere (tarihî < güncelleme <= eşik) bellekte uygulanır, sonra parti sınırı. (Repo sayfa boyutunu 500'e kırpar.)
        var page = await complaints.ListAsync(new ComplaintQuery(ComplaintQuery.OpenStatus, PageSize: 500), ct);
        var window = page.Items.Where(i => i.Complaint.UpdatedAt <= cutoff && i.Complaint.UpdatedAt >= floor)
            .Take(Math.Max(1, o.BatchSize)).Select(i => (i.ComplaintId, i.Complaint));
        foreach (var (complaintId, c) in window)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!long.TryParse(c.CustomerId, out var customerId)) continue;
                var key = $"proactive:stale-complaint:{complaintId}";
                if (!await ledger.TryClaimAsync(key, ct)) continue;

                var days = Math.Max(1, (int)Math.Floor((now - c.UpdatedAt).TotalDays));
                hub?.Publish(c.CustomerId, CustomerNotifications.ForStaleComplaint(complaintId, c, days, now));
                notified++;

                if (!email.Value.Enabled || !sender.IsEnabled) continue;
                if (!await SendAsync(key, async address =>
                    ProactiveEmailComposer.StaleComplaint(complaintId, c, days, address, await customers.GetFullNameAsync(customerId, ct), email.Value.PublicBaseUrl),
                    customerId, complaintId, ct)) notified--;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Proactive] Bekleyen şikayet bildirimi işlenemedi | complaint={ComplaintId}", complaintId);
            }
        }
        return notified;
    }

    private Task<bool> SendEmailAsync(string key, string orderId, Domain.Model.OrderInfo order, long customerId, int days, CancellationToken ct) =>
        SendAsync(key, async address => ProactiveEmailComposer.DelayedShipment(
            orderId, order, days, address, await customers.GetFullNameAsync(customerId, ct), email.Value.PublicBaseUrl), customerId, orderId, ct);

    private async Task<bool> SendAsync(string key, Func<string, Task<EmailMessage>> compose, long customerId, string reference, CancellationToken ct)
    {
        try
        {
            var address = await customers.GetEmailAsync(customerId, ct);
            if (string.IsNullOrWhiteSpace(address)) return true; // talep tutulur: adres yokken yeniden denemenin anlamı yok

            await sender.SendAsync(await compose(address), ct);
            logger.LogInformation("[Proactive] E-posta gönderildi | ref={Reference} to={To}", reference, PiiMasker.MaskEmail(address));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Proactive] E-posta gönderilemedi | ref={Reference}", reference);
            try { await ledger.ReleaseAsync(key, CancellationToken.None); }
            catch (Exception releaseEx) { logger.LogWarning(releaseEx, "[Proactive] Defter kaydı geri bırakılamadı | key={Key}", key); }
            return false;
        }
    }
}
