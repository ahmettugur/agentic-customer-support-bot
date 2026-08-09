using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// HITL onay kuyruğu için secondary port.
///</summary>
public interface IApprovalQueue
{
    Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default);
    Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default);
    Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default);
    IReadOnlyList<ApprovalRequest> GetPending();
    IReadOnlyList<ApprovalRequest> GetRecent(int count = 50);
    ApprovalRequest? Get(string id);

    /// <summary>
    /// Bu session'a ait, karara bağlanmış (Approved/Rejected/Expired) ama müşterinin henüz
    /// bildirim olarak görmediği (CustomerSeenAt == null) kayıtlar. Kullanıcı chat'e geri
    /// döndüğünde (yeni sekme/sayfa yenileme) kaçırdığı onay sonuçlarını görebilsin diye.
    /// </summary>
    IReadOnlyList<ApprovalRequest> GetUnseenForSession(string sessionId);

    /// <summary>Kullanıcı bildirimi gördüğünde CustomerSeenAt'i işaretler.</summary>
    Task MarkSeenAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Bu müşteriye ait TÜM onay taleplerini (görülmüş/görülmemiş, karara bağlanmış/bekleyen
    /// fark etmeksizin) en yeniden eskiye sıralı döner — "geçmiş işlemlerim" görünümü için.
    /// SessionId'ye değil CustomerId'ye göre sorgular, bu yüzden müşteri farklı bir cihazda/
    /// sekmede tekrar login olsa bile aynı geçmişi görür.
    /// </summary>
    IReadOnlyList<ApprovalRequest> GetHistoryForCustomer(string customerId, int count = 100);

    event EventHandler<ApprovalRequest>? RequestCreated;
    event EventHandler<ApprovalRequest>? RequestDecided;
}
