// Ports/Outbound/IApprovalExecutionRouter.cs
// Admin bir onay talebini ONAYLADIĞINDA gerçek işi (sipariş iptali, iade vb.)
// tetikleyen dispatcher. IApprovalQueue.DecideAsync tarafından çağrılır —
// tool çağrısının kendisi artık bu kararı beklemediği için (bkz. ApprovalGateService)
// gerçek yürütme burada, karar anında gerçekleşir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

public interface IApprovalExecutionRouter
{
    Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default);
}

public sealed record ApprovalExecutionOutcome(bool Success, string Message);
