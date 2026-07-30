using System.ComponentModel;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

/// <summary>
/// Şikayet yönetimi araçları uygulama servisi.
/// </summary>
public sealed class ComplaintToolsService : IComplaintToolsService
{
    private readonly IComplaintRepository _complaints;
    private readonly IOrderRepository _orders;
    private readonly SideEffectIdempotencyCache _idempotency;

    public ComplaintToolsService(
        IComplaintRepository complaints,
        IOrderRepository orders,
        SideEffectIdempotencyCache? idempotency = null)
    {
        _complaints = complaints;
        _orders = orders;
        _idempotency = idempotency ?? new SideEffectIdempotencyCache();
    }

    [Description("Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur; " +
                 "customer_id opsiyoneldir (boşsa order_id üzerinden siparişten otomatik türetilir). " +
                 "Sonuç ToolResult olarak döner.")]
    public ToolResult ComplaintRegistrationTool(
        [Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu, ör. '1030')")] string orderId,
        [Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText,
        [Description("Müşteri kimlik numarası (opsiyonel; boşsa siparişten türetilir)")] string? customerId = null)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(orderId)) missing.Add(WellKnown.ToolParameterNames.OrderId);
        if (string.IsNullOrWhiteSpace(complaintText) || complaintText.Length < 10)
            missing.Add($"{WellKnown.ToolParameterNames.ComplaintDescription} (en az 10 karakter)");
        if (missing.Count > 0)
            return ToolResult.ValidationError($"Şikayet kaydı için şu bilgiler gerekli: {string.Join(", ", missing)}.", missing.ToArray());

        var order = _orders.Get(orderId);
        if (order is null)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.OrderNotFound, $"'{orderId}' numaralı sipariş bulunamadı, şikayet kaydı oluşturulamadı.");

        var effectiveCustomerId = customerId;
        var inferred = false;
        if (string.IsNullOrWhiteSpace(effectiveCustomerId))
        {
            effectiveCustomerId = order.CustomerId;
            inferred = true;
        }
        else if (!string.Equals(effectiveCustomerId, order.CustomerId, StringComparison.Ordinal))
        {
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.CustomerIdMismatch,
                $"Sağladığınız müşteri kimliği ({effectiveCustomerId}) '{orderId}' siparişinin sahibiyle eşleşmiyor.");
        }

        // Mükerrer çağrı koruması — kayıt oluşturulmadan ÖNCE. İmza türetilmiş
        // customerId üzerinden kurulur, böylece customerId'nin verilip verilmemesi
        // aynı şikayeti iki farklı çağrı gibi göstermez.
        var signature = new object?[] { orderId, effectiveCustomerId, complaintText };
        if (_idempotency.TryGetRecent(WellKnown.ToolNames.ComplaintRegistration, signature, out var recent))
        {
            return ToolResult.Ok(
                message: $"Bu şikayeti az önce kaydetmiştim — şikayet numarası: {recent.EntityId}. " +
                         "Mükerrer kayıt oluşturmadım. Gerçekten ikinci bir şikayet kaydı istiyorsanız lütfen açıkça belirtin.",
                data: new
                {
                    complaintId = recent.EntityId,
                    orderId,
                    customerId = effectiveCustomerId,
                    customerIdInferred = inferred,
                    status = WellKnown.ComplaintStatuses.Pending,
                    duplicate = true
                },
                confidence: 0.9);
        }

        var complaintId = _complaints.Create(new ComplaintInfo
        {
            OrderId    = orderId,
            CustomerId = effectiveCustomerId!,
            Complaint  = complaintText,
            Status     = WellKnown.ComplaintStatuses.Pending
        });

        var result = ToolResult.Ok(
            message: $"Şikayet başarıyla kaydedildi! Şikayet numarası: {complaintId}",
            data: new { complaintId, orderId, customerId = effectiveCustomerId, customerIdInferred = inferred, status = WellKnown.ComplaintStatuses.Pending });

        _idempotency.Record(WellKnown.ToolNames.ComplaintRegistration, signature, result, complaintId);
        return result;
    }
}
