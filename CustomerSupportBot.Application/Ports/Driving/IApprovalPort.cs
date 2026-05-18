// Ports/Driving/IApprovalPort.cs
// PRIMARY PORT — HITL onay akışı.
// AdminEndpoints bu port üzerinden pending onayları listeler ve karar verir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Human-in-the-Loop onay akışı için primary port.
/// Admin panel adaptörü (AgentPanelEndpoints) bu arayüzü kullanır.
/// </summary>
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
