using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Şikayet yönetimi araçları için secondary port.
/// </summary>
public interface IComplaintToolsService
{
    ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId);

    /// <summary>
    /// Şikayet durumunu şikayet numarasıyla sorgular (SALT-OKUNUR).
    ///
    /// <para>
    /// <paramref name="customerId"/> sahiplik kontrolü içindir ve LLM'den DEĞİL, doğrulanmış
    /// kimlikten gelir. Başkasının şikayetine erişim, "bulunamadı" ile <b>aynı metni</b> döner —
    /// bkz. <c>ComplaintToolsService</c>.
    /// </para>
    /// </summary>
    ToolResult ComplaintStatusTool(string complaintId, string customerId);

    /// <summary>Müşterinin tüm şikayetlerini listeler (SALT-OKUNUR).</summary>
    ToolResult GetAllComplaintsTool(string customerId);
}
