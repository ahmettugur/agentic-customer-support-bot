// Ports/Driven/Persistence/IApprovalQueueRepository.cs
// SECONDARY PORT — HITL onay kuyruğu kalıcılığı.
// Mevcut IApprovalQueue interface'i bu port'a taşınır.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// HITL onay kuyruğu için secondary port.
/// Adaptörler: PostgresApprovalQueue, InMemoryApprovalQueue.
/// </summary>
public interface IApprovalQueueRepository
{
    ApprovalRequest Create(ApprovalRequest request);
    Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default);
    bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null);
    IReadOnlyList<ApprovalRequest> GetPending();
    IReadOnlyList<ApprovalRequest> GetRecent(int count = 50);
    ApprovalRequest? Get(string id);

    event EventHandler<ApprovalRequest>? RequestCreated;
    event EventHandler<ApprovalRequest>? RequestDecided;
}
