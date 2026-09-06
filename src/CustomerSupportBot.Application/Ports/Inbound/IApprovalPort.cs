using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Human-in-the-Loop onay akışı için primary port.
///</summary>
public interface IApprovalPort
{
    /// <summary>
    /// Bekleyen onay istekleri — <see cref="ApprovalRequest.CustomerName"/> doldurulmuş olarak.
    /// </summary>
    /// <remarks>
    /// Async olmasının sebebi müşteri adlarının veritabanından çözülmesidir; kuyruğun kendisi
    /// bellek içi cache'ten gelir. Ad çözümü tek bir toplu sorgudur (N+1 değil).
    /// </remarks>
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Son N onay geçmişi — <see cref="ApprovalRequest.CustomerName"/> doldurulmuş olarak.</summary>
    Task<IReadOnlyList<ApprovalRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default);

    /// <summary>
    /// Onaylanmış ama yürütmesi askıda kalmış kayıtlar — admin panelinin elle müdahale için
    /// göstermesi gerekenler. Tarih sınırı yoktur (bkz. IApprovalQueue.GetStuckExecutionsAsync).
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default);

    /// <summary>Tek istek.</summary>
    ApprovalRequest? Get(string id);

    /// <summary>Reads the durable request for decision and authorization checks.</summary>
    Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Admin kararını uygular (approve / reject).
    /// Request Pending değilse false döner (idempotent).
    /// </summary>
    Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default);
}
