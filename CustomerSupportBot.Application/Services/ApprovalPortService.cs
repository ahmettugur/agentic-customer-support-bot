// Application/Services/ApprovalPortService.cs
// DRIVING PORT IMPL — IApprovalPort → HITL onay kuyruğu orkestrasyonu.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// HITL onay akışı driving port implementasyonu.
/// Admin panel (AgentPanelEndpoints) bu sınıfı IApprovalPort olarak kullanır.
/// </summary>
public sealed class ApprovalPortService : IApprovalPort
{
    private readonly IApprovalQueue _approvalQueue;
    private readonly ILogger<ApprovalPortService> _logger;

    public ApprovalPortService(
        IApprovalQueue approvalQueue,
        ILogger<ApprovalPortService> logger)
    {
        _approvalQueue = approvalQueue;
        _logger = logger;
    }

    public IReadOnlyList<ApprovalRequest> GetPending()
    {
        return _approvalQueue.GetPending();
    }

    public IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)
    {
        return _approvalQueue.GetRecent(count);
    }

    public ApprovalRequest? Get(string id)
    {
        return _approvalQueue.Get(id);
    }

    public bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null)
    {
        var request = _approvalQueue.Get(id);
        if (request is null)
        {
            _logger.LogWarning("Approval request not found: {Id}", id);
            return false;
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            _logger.LogDebug("Approval request already decided: {Id} status={Status}", id, request.Status);
            return false;
        }

        var result = _approvalQueue.Decide(id, approved, decidedBy, reason);
        
        if (result)
        {
            _logger.LogInformation(
                "Approval decision applied: {Id} approved={Approved} by={DecidedBy}",
                id, approved, decidedBy ?? "unknown");
        }

        return result;
    }
}
