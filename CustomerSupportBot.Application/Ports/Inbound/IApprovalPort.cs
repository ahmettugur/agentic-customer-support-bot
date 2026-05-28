using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Human-in-the-Loop onay akışı için primary port.
///</summary>
public interface IApprovalPort
{
    /// <summary>Bekleyen onay istekleri.</summary>
    IReadOnlyList<ApprovalRequest> GetPending();

    /// <summary>Son N onay geçmişi.</summary>
    IReadOnlyList<ApprovalRequest> GetRecent(int count = 50);

    /// <summary>Tek istek.</summary>
    ApprovalRequest? Get(string id);

    /// <summary>
    /// Admin kararını uygular (approve / reject).
    /// Request Pending değilse false döner (idempotent).
    /// </summary>
    bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null);
}
