// Services/Providers/CustomerContextProvider.cs
// Müşteri verilerini FakeDatabase'den çekerek ajanlara bağlam sağlar.

using System.Text;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services.Providers;

/// <summary>
/// Oturumdaki CustomerId bilgisine göre müşterinin sipariş ve şikayet
/// Geçmişini FakeDatabase'den çeker ve bağlam olarak sunar.
/// </summary>
public class CustomerContextProvider : IContextProvider
{
    public string Name => "CustomerContext";
    public int Order => 10;

    public Task<string?> GetContextAsync(AgentSession session)
    {
        var customerId = session.State.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        sb.AppendLine($"[Müşteri Bağlamı — {customerId}]");

        // Siparişler
        var orders = FakeDatabase.GetAllOrders(customerId).ToList();
        if (orders.Count > 0)
        {
            sb.AppendLine($"Toplam sipariş: {orders.Count}");
            foreach (var (orderId, order) in orders.Take(5))
            {
                sb.AppendLine($"  - {orderId}: {order.Product} x{order.Quantity}, " +
                              $"Durum: {order.Status}, Tarih: {order.OrderDate:g}");
            }
        }
        else
        {
            sb.AppendLine("Kayıtlı sipariş bulunamadı.");
        }

        // Şikayetler
        var complaints = FakeDatabase.ComplaintsDb
            .Where(c => c.Value.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (complaints.Count > 0)
        {
            sb.AppendLine($"Toplam şikayet: {complaints.Count}");
            foreach (var (complaintId, complaint) in complaints.Take(3))
            {
                sb.AppendLine($"  - {complaintId}: Sipariş {complaint.OrderId}, " +
                              $"Durum: {complaint.Status}");
            }
        }

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }
}
