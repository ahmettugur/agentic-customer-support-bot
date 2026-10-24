// Application/Services/Providers/CustomerOpenMattersContextProvider.cs
// IContextProvider — login'li müşterinin KAYITTAKİ açık konularını (son sipariş, açık şikayet, bekleyen iade) bağlama ekler.
//
// CustomerProfile provider'ı davranıştan ÇIKARIM yapar; bu provider veritabanındaki KESİN durumu verir. Böylece bot müşteri
// sormadan neyin açık olduğunu bilir ("Merhaba, 1057 numaralı siparişiniz yolda; şikayetiniz incelemede").
//
// Güvenlik: yalnızca JWT'ye bağlı kimlik (AuthenticatedCustomerId) kullanılır — LLM'in metinden çıkardığı CustomerId asla.
// Müşterinin serbest metni (şikayet açıklaması) bağlama GİRMEZ; yalnızca durum/numara/tarih ve sanitize edilmiş kargo bilgisi.

using System.Text;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class CustomerOpenMattersContextProvider(
    IOrderRepository orders,
    IComplaintRepository complaints,
    IContextSanitizer sanitizer,
    ILogger<CustomerOpenMattersContextProvider> logger,
    TimeProvider? clock = null) : IContextProvider
{
    private const int MaxItemsPerKind = 3;
    private const int RecentOrderDays = 30;
    private const int FieldMaxLength = 64;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string Name => "CustomerOpenMatters";

    /// <summary>Profil (6) ile aynı öncelik; kayıt sırasıyla profilden sonra gelir. Bütçe dolarsa semantik bilgiden önce korunur.</summary>
    public int Order => 6;

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var customerId = session.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return Task.FromResult<string?>(null);

        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var lines = new List<string>();

            var myOrders = orders.GetByCustomer(customerId);   // en yeniden eskiye
            if (myOrders.Count > 0)
            {
                var (id, last) = myOrders[0];
                if (last.Status != WellKnown.OrderStatuses.Delivered || (now - last.OrderDate).TotalDays <= RecentOrderDays)
                    lines.Add($"- Son sipariş #{id}: {Clean(last.Status)}{ShippingDetail(last, now)}");

                foreach (var (rid, r) in myOrders.Skip(1).Where(o => IsPendingReturn(o.Order)).Take(MaxItemsPerKind))
                    lines.Add($"- Bekleyen iade #{rid}: {Clean(r.Status)}");
            }

            foreach (var (cid, c) in complaints.GetByCustomer(customerId)
                         .Where(x => WellKnown.ComplaintStatuses.IsOpen(x.Complaint.Status))
                         .OrderByDescending(x => x.Complaint.UpdatedAt).Take(MaxItemsPerKind))
                lines.Add($"- Açık şikayet {Clean(cid)} (sipariş #{Clean(c.OrderId)}): {Clean(c.Status)}, son güncelleme {DaysAgo(c.UpdatedAt, now)}");

            if (lines.Count == 0) return Task.FromResult<string?>(null);

            var sb = new StringBuilder()
                .AppendLine("## 📌 Müşterinin Açık Konuları (kayıttan — kesin bilgi)")
                .AppendLine("Müşteri sormasa da ilgiliyse kısaca anabilirsiniz; ayrıntı için ilgili aracı çağırın, buradan tahmin yürütmeyin.");
            foreach (var l in lines) sb.AppendLine(l);
            return Task.FromResult<string?>(sb.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bağlam eksik kalır ama tur bozulmaz.
            logger.LogWarning(ex, "[OpenMatters] Açık konular okunamadı | customer={CustomerId}", customerId);
            return Task.FromResult<string?>(null);
        }
    }

    private static bool IsPendingReturn(OrderInfo o) =>
        o.Status is WellKnown.OrderStatuses.ReturnRequested or WellKnown.OrderStatuses.ReturnApproved;

    private string ShippingDetail(OrderInfo o, DateTime now)
    {
        if (o.Status != WellKnown.OrderStatuses.Shipped) return "";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.Carrier)) parts.Add($"kargo: {Clean(o.Carrier)}");
        if (!string.IsNullOrWhiteSpace(o.TrackingNumber)) parts.Add($"takip no: {Clean(o.TrackingNumber)}");
        if (o.ShippedAt is { } at) parts.Add($"{DaysAgo(at, now)} kargolandı");
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    private string Clean(string? s) => sanitizer.Sanitize(s ?? "", FieldMaxLength).Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string DaysAgo(DateTime then, DateTime now)
    {
        var days = (int)Math.Floor((now - then).TotalDays);
        return days <= 0 ? "bugün" : $"{days} gün önce";
    }
}
