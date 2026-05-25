using System.ComponentModel;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Şikayet yönetimi araçları uygulama servisi.
/// </summary>
public sealed class ComplaintToolsService : IComplaintToolsService
{
    private readonly IComplaintRepository _complaints;
    private readonly IOrderRepository _orders;

    public ComplaintToolsService(IComplaintRepository complaints, IOrderRepository orders)
    {
        _complaints = complaints;
        _orders = orders;
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

        var complaintId = _complaints.Create(new ComplaintInfo
        {
            OrderId    = orderId,
            CustomerId = effectiveCustomerId!,
            Complaint  = complaintText,
            Status     = WellKnown.ComplaintStatuses.Pending
        });

        return ToolResult.Ok(
            message: $"Şikayet başarıyla kaydedildi! Şikayet numarası: {complaintId}",
            data: new { complaintId, orderId, customerId = effectiveCustomerId, customerIdInferred = inferred, status = WellKnown.ComplaintStatuses.Pending });
    }
}
