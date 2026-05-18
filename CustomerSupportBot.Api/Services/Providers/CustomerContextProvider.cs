// Services/Providers/CustomerContextProvider.cs
// Müþteri verilerini port'lar üzerinden çekerek ajanlara baðlam saðlar.

using System.Text;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;

namespace CustomerSupportBot.Api.Services.Providers;

/// <summary>
/// Oturumdaki CustomerId bilgisine göre müþterinin sipariþ ve þikayet
/// geçmiþini repository port'larýndan çeker ve baðlam olarak sunar.
/// </summary>
public class CustomerContextProvider : IContextProvider
{
    private readonly IOrderRepository _orders;
    private readonly IComplaintRepository _complaints;

    public CustomerContextProvider(IOrderRepository orders, IComplaintRepository complaints)
    {
        _orders = orders;
        _complaints = complaints;
    }

    public string Name => "CustomerContext";
    public int Order => 10;

    public Task<string?> GetContextAsync(AgentSession session)
    {
        var customerId = session.State.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        sb.AppendLine($"[Müþteri Baðlamý – {customerId}]");

        var orders = _orders.GetByCustomer(customerId).ToList();
        if (orders.Count > 0)
        {
            sb.AppendLine($"Toplam sipariþ: {orders.Count}");
            foreach (var (orderId, order) in orders.Take(5))
            {
                sb.AppendLine($"  - {orderId}: {order.Product} x{order.Quantity}, " +
                              $"Durum: {order.Status}, Tarih: {order.OrderDate:g}");
            }
        }
        else
        {
            sb.AppendLine("Kayýtlý sipariþ bulunamadý.");
        }

        var complaints = _complaints.GetByCustomer(customerId).ToList();
        if (complaints.Count > 0)
        {
            sb.AppendLine($"Toplam þikayet: {complaints.Count}");
            foreach (var (complaintId, complaint) in complaints.Take(3))
            {
                sb.AppendLine($"  - {complaintId}: Sipariþ {complaint.OrderId}, " +
                              $"Durum: {complaint.Status}");
            }
        }

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }
}
