using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Şikayet yönetimi için secondary port.
/// </summary>
public interface IComplaintRepository
{
    /// <summary>Yeni şikayet oluşturur ve şikayet ID'sini döner.</summary>
    string Create(ComplaintInfo complaint);

    /// <summary>Şikayet ID ile sorgular. Bulunamazsa null döner.</summary>
    ComplaintInfo? Get(string complaintId);

    /// <summary>Sipariş ID'ye göre şikayetler.</summary>
    IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId);

    /// <summary>Müşteri ID'ye göre tüm şikayetler.</summary>
    IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId);
}
