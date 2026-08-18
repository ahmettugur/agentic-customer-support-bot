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
        // Trim: boşluk dolgusuyla min-uzunluk kuralı aşılamamalı.
        if (string.IsNullOrWhiteSpace(complaintText) || complaintText.Trim().Length < 10)
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
            // Kullanıcıya giden metin "sipariş yok" ile AYNI — "var ama başkasının" ayrımı
            // sızdırılmaz (enumeration oracle'ı; bkz. OrderToolsService.OrderNotAccessibleMessage).
            // Ayrıca customerId artık kullanıcının "sağladığı" bir şey değil (JWT'den gelir),
            // bu yüzden eski "Sağladığınız müşteri kimliği (X)..." metni hem yanıltıcıydı hem de
            // kullanıcıya kendi iç kimlik numarasını gösteriyordu.
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.CustomerIdMismatch,
                $"'{orderId}' numaralı sipariş bulunamadı, şikayet kaydı oluşturulamadı.");
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
    /// <summary>
    /// Şikayet erişilemediğinde dönen TEK metin.
    ///
    /// <para>
    /// Sahiplik ihlali ile "hiç yok" durumu <b>aynı metni</b> döndürür — sipariş tarafındaki
    /// <c>OrderNotAccessibleMessage</c> ile aynı gerekçe: ayrı metinler dönseydi, dışarıdan
    /// şikayet numarası taranarak hangi numaraların var olduğu (ve dolaylı olarak başka
    /// müşterilerin şikayet hacmi) öğrenilebilirdi. Hata KODU farklıdır
    /// (<c>CustomerIdMismatch</c> vs <c>ComplaintNotFound</c>), böylece trace/admin panelinde
    /// gerçek sebep görünür; kullanıcıya giden metin aynıdır.
    /// </para>
    /// </summary>
    private static string ComplaintNotAccessibleMessage(string complaintId) =>
        $"'{complaintId}' numaralı şikayet bulunamadı.";

    [Description("Şikayet durumunu şikayet numarasıyla sorgular (salt-okunur). Sonuç ToolResult olarak döner.")]
    public ToolResult ComplaintStatusTool(
        [Description("Sorgulanacak şikayet numarası (örn: 1001)")] string complaintId,
        string customerId)
    {
        if (string.IsNullOrWhiteSpace(complaintId))
            return ToolResult.ValidationError("Şikayet numarası boş olamaz.", WellKnown.ToolParameterNames.ComplaintId);

        if (string.IsNullOrWhiteSpace(customerId))
            return ToolResult.ValidationError("Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);

        var complaint = _complaints.Get(complaintId);
        if (complaint is null)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ComplaintNotFound, ComplaintNotAccessibleMessage(complaintId));

        // Sahiplik ihlali "bulunamadı" ile AYNI metni döner — bkz. ComplaintNotAccessibleMessage.
        if (!string.Equals(complaint.CustomerId, customerId, StringComparison.Ordinal))
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.CustomerIdMismatch, ComplaintNotAccessibleMessage(complaintId));

        return ToolResult.Ok(
            message: $"Şikayet No: {complaintId}, Sipariş: {complaint.OrderId}, Durum: {complaint.Status}.",
            data: new
            {
                complaintId,
                orderId = complaint.OrderId,
                status = complaint.Status,
                complaint = complaint.Complaint
            });
    }

    [Description("Müşterinin tüm şikayetlerini listeler (salt-okunur). Sonuç ToolResult olarak döner.")]
    public ToolResult GetAllComplaintsTool(
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return ToolResult.ValidationError("Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);

        var all = _complaints.GetByCustomer(customerId);
        if (all.Count == 0)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.NoComplaintsForCustomer,
                "Kayıtlı bir şikayetiniz bulunmamaktadır.");

        var lines = all.Select(c => $"{c.ComplaintId}: Sipariş {c.Complaint.OrderId}, Durum: {c.Complaint.Status}");

        return ToolResult.Ok(
            message: $"Toplam {all.Count} şikayet: " + string.Join(" | ", lines),
            data: new
            {
                count = all.Count,
                complaints = all.Select(c => new
                {
                    complaintId = c.ComplaintId,
                    orderId = c.Complaint.OrderId,
                    status = c.Complaint.Status
                }).ToList()
            });
    }

}
