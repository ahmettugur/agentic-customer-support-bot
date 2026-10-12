using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <param name="Status">null = tümü; <see cref="OpenStatus"/> = Beklemede + İncelemede; aksi hâlde tek durum.</param>
/// <param name="AssignedAgentId">Yalnızca bu temsilciye atananlar.</param>
/// <param name="VisibleToAgentId">Temsilci kapsamı: atanmamış + bu temsilciye atanan.</param>
public sealed record ComplaintQuery(
    string? Status = null,
    string? AssignedAgentId = null,
    bool UnassignedOnly = false,
    string? VisibleToAgentId = null,
    int Page = 1,
    int PageSize = 20)
{
    public const string OpenStatus = "open";
}

public sealed record ComplaintListItem(string ComplaintId, ComplaintInfo Complaint);

public sealed record ComplaintPage(IReadOnlyList<ComplaintListItem> Items, int Total);

public sealed record ComplaintCounts(int Pending, int InProgress, int AssignedToAgent);

/// <param name="Resolution">null = mevcut açıklamayı koru.</param>
public sealed record ComplaintTransition(
    string ComplaintId,
    string ExpectedStatus,
    int ExpectedVersion,
    string NewStatus,
    string? AssignedAgentId,
    string? Resolution,
    ComplaintEvent Event);

public enum ComplaintTransitionChange { Updated, Conflict, NotFound }

public sealed record ComplaintTransitionOutcome(ComplaintTransitionChange Change, ComplaintInfo? Complaint);

/// <summary>
/// Şikayet yönetimi için secondary port.
/// </summary>
public interface IComplaintRepository
{
    /// <summary>Yeni şikayet oluşturur ("created" olayıyla) ve şikayet ID'sini döner.</summary>
    string Create(ComplaintInfo complaint);

    /// <summary>Şikayet ID ile sorgular. Bulunamazsa null döner.</summary>
    ComplaintInfo? Get(string complaintId);

    /// <summary>Sipariş ID'ye göre şikayetler.</summary>
    IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId);

    /// <summary>Müşteri ID'ye göre tüm şikayetler.</summary>
    IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId);

    /// <summary>Panel listesi: açıklar en eskiden yeniye, kapalılar en yeniden eskiye.</summary>
    Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default);

    /// <summary>Bekleyen, incelemede ve (agentId verilirse) o temsilciye atanan açık şikayet sayıları.</summary>
    Task<ComplaintCounts> CountOpenAsync(string? agentId, CancellationToken ct = default);

    /// <summary>Açık şikayetler (SLA taraması) — en eskiden.</summary>
    Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(int max = 500, CancellationToken ct = default);

    /// <summary>Zaman çizelgesi (eskiden yeniye).</summary>
    Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(string complaintId, CancellationToken ct = default);

    /// <summary>
    /// Koşullu geçiş: durum VE sürüm beklenen değerdeyse günceller ve olayı aynı transaction'da yazar;
    /// değilse hiçbir şey yazmaz (Conflict).
    /// </summary>
    Task<ComplaintTransitionOutcome> TryTransitionAsync(ComplaintTransition transition, CancellationToken ct = default);

    /// <summary>Durumu değiştirmeden olay (iç not) ekler; şikayet yoksa false.</summary>
    Task<bool> AddEventAsync(string complaintId, ComplaintEvent evt, CancellationToken ct = default);
}
