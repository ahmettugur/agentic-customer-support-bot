// Application/Services/Providers/CustomerContextProvider.cs
// Müşteri verilerini port'lar üzerinden çekerek ajanlara bağlam sağlar.

using System.Text;

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

/// <summary>
/// Login'li müşterinin sipariş ve şikayet geçmişini repository port'larından çeker ve
/// bağlam olarak sunar.
///
/// <para>
/// Kimlik <see cref="SessionState.AuthenticatedCustomerId"/>'den (JWT) okunur —
/// <see cref="SessionState.CustomerId"/> (LLM'in kullanıcı metninden çıkardığı, kullanıcının
/// "ben 1008 numaralı müşteriyim" diyerek değiştirebildiği alan) KULLANILMAZ. Aksi halde bu
/// sağlayıcı başka bir müşterinin tüm sipariş/şikayet geçmişini ajanın context'ine enjekte
/// eder ve ajan bunu kullanıcıya okur — tool katmanındaki sahiplik kontrollerini tamamen
/// baypas eden bir veri sızıntısı olurdu.
/// </para>
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

    /// <summary>
    /// Kritik: bu bağlam düşerse model müşterinin siparişlerini göremez ve büyük olasılıkla
    /// "kayıtlı siparişiniz bulunamadı" der — yani altyapı hatası kullanıcıya yanlış olgu
    /// olarak yansır. Sessizce atlamak yerine modele eksikliği bildirilir.
    /// </summary>
    public bool IsCritical => true;

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var customerId = session.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        sb.AppendLine($"[Müşteri Bağlamı — {customerId}]");

        var orders = _orders.GetByCustomer(customerId).ToList();
        if (orders.Count > 0)
        {
            sb.AppendLine($"Toplam sipariş: {orders.Count}");
            foreach (var (orderId, order) in orders.Take(5))
            {
                sb.AppendLine($"  - {orderId}: {order.LinesSummary()}, " +
                              $"Durum: {order.Status}, Tarih: {order.OrderDate:g}");
            }
        }
        else
        {
            sb.AppendLine("Kayıtlı sipariş bulunamadı.");
        }

        var complaints = _complaints.GetByCustomer(customerId).ToList();
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
