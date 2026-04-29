// Services/InMemoryEscalationSink.cs
// HITL — In-memory escalation store. Ring buffer (max 500), thread-safe.
// Production için Redis/DB/Slack/Zendesk adapter ile değiştirilebilir.

using System.Collections.Concurrent;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

public class InMemoryEscalationSink : IEscalationSink
{
    private readonly ConcurrentDictionary<string, EscalationRequest> _byId = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly ILogger<InMemoryEscalationSink> _logger;
    private const int Capacity = 500;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    public InMemoryEscalationSink(ILogger<InMemoryEscalationSink> logger)
    {
        _logger = logger;
    }

    public EscalationRequest Create(EscalationRequest request)
    {
        _byId[request.Id] = request;
        _order.Enqueue(request.Id);
        Trim();

        _logger.LogWarning(
            "[HITL] Escalation created: id={Id}, agent={Agent}, reason={Reason}",
            request.Id, request.AgentName, request.Reason);

        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

        return request;
    }

    public IReadOnlyList<EscalationRequest> GetOpen() =>
        _byId.Values
            .Where(e => e.Status == EscalationStatus.Open || e.Status == EscalationStatus.Acknowledged)
            .OrderBy(e => e.CreatedAt)
            .ToList();

    public IReadOnlyList<EscalationRequest> GetRecent(int count = 50) =>
        _byId.Values
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList();

    public EscalationRequest? Get(string id) =>
        _byId.TryGetValue(id, out var e) ? e : null;

    public bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)
    {
        if (!_byId.TryGetValue(id, out var req)) return false;

        // Aksiyon string'ini normalize et (altyapı katmanı sorumluluğu)
        var normalized = (action ?? "").Trim().ToLowerInvariant();

        // Mevcut duruma ait state nesnesini al
        var state = EscalationStateFactory.Create(req.Status);

        // Aksiyona göre ilgili state metodunu çağır — geçiş kuralları state sınıfında
        var success = normalized switch
        {
            WellKnown.EscalationActions.Acknowledge
                or WellKnown.EscalationActions.Ack => state.Acknowledge(req, assignedTo),
            WellKnown.EscalationActions.Resolve => state.Resolve(req, assignedTo, resolution),
            WellKnown.EscalationActions.Dismiss => state.Dismiss(req, assignedTo, resolution),
            _ => false
        };

        if (!success) return false;

        _logger.LogInformation(
            "[HITL] Escalation decided: id={Id}, action={Action}, status={Status}",
            id, normalized, req.Status);

        try { RequestDecided?.Invoke(this, req); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

        return true;
    }

    private void Trim()
    {
        while (_order.Count > Capacity)
        {
            if (!_order.TryDequeue(out var oldId)) break;
            _byId.TryRemove(oldId, out _);
        }
    }
}
