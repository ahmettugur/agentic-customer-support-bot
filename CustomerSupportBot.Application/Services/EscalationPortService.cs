// Application/Services/EscalationPortService.cs
// DRIVING PORT IMPL — IEscalationPort → Eskalasyon yönetimi orkestrasyonu.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Eskalasyon yönetimi driving port implementasyonu.
/// Admin panel (AdminEndpoints) bu sınıfı IEscalationPort olarak kullanır.
/// </summary>
public sealed class EscalationPortService : IEscalationPort
{
    private readonly IEscalationRepository _escalations;
    private readonly ILogger<EscalationPortService> _logger;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    public EscalationPortService(
        IEscalationRepository escalations,
        ILogger<EscalationPortService> logger)
    {
        _escalations = escalations;
        _logger = logger;

        // Driven port event'lerini driving port'a bridge et
        _escalations.RequestCreated += (_, req) => RequestCreated?.Invoke(this, req);
        _escalations.RequestDecided += (_, req) => RequestDecided?.Invoke(this, req);
    }

    public EscalationRequest Create(EscalationRequest request)
    {
        var created = _escalations.Create(request);
        _logger.LogInformation(
            "Escalation created: {Id} session={SessionId} reason={Reason}",
            created.Id, created.SessionId, created.Reason);
        return created;
    }

    public IReadOnlyList<EscalationRequest> GetOpen()
    {
        return _escalations.GetOpen();
    }

    public IReadOnlyList<EscalationRequest> GetRecent(int count = 50)
    {
        return _escalations.GetRecent(count);
    }

    public EscalationRequest? Get(string id)
    {
        return _escalations.Get(id);
    }

    public bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)
    {
        var request = _escalations.Get(id);
        if (request is null)
        {
            _logger.LogWarning("Escalation not found: {Id}", id);
            return false;
        }

        var result = _escalations.Decide(id, action, assignedTo, resolution);

        if (result)
        {
            _logger.LogInformation(
                "Escalation decision applied: {Id} action={Action} assignedTo={AssignedTo}",
                id, action, assignedTo ?? "unassigned");
        }

        return result;
    }
}
