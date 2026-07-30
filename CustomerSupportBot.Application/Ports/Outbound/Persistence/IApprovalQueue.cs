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

    event EventHandler<ApprovalRequest>? RequestCreated;
    event EventHandler<ApprovalRequest>? RequestDecided;
}
