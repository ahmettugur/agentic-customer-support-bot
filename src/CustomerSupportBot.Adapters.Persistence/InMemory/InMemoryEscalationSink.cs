// Services/InMemoryEscalationSink.cs
// HITL — In-memory escalation store. Ring buffer (max 500), thread-safe.
// Production için Redis/DB/Slack/Zendesk adapter ile değiştirilebilir.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public class InMemoryEscalationSink : IEscalationSink
{
    private readonly ConcurrentDictionary<string, EscalationRequest> _byId = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly ILogger<InMemoryEscalationSink> _logger;
    // Create'in "açık kayıt var mı?" kontrolü ile eklemeyi, Decide'ın durum okuma ile
    // geçişi tek adımda yapması için. Postgres adaptöründe aynı garantiyi unique index ve
    // koşullu UPDATE verir; burada tek süreç olduğundan bir kilit yeterli.
    private readonly object _gate = new();
    private const int Capacity = 500;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    public InMemoryEscalationSink(ILogger<InMemoryEscalationSink> logger)
    {
        _logger = logger;
    }

    public Task<EscalationRequest> CreateAsync(EscalationRequest request)
        => Task.FromResult(Create(request));

    private EscalationRequest Create(EscalationRequest request)
    {
        lock (_gate)
        {
            // Session + ajan başına tek açık eskalasyon (Postgres'teki
            // ux_escalations_open_session_agent ile aynı kural). Session'sız kayıtlar
            // dedup'a girmez — DB'de de NULL'lar birbirini engellemez.
            if (!string.IsNullOrEmpty(request.SessionId) && !string.IsNullOrEmpty(request.AgentName))
            {
                var existing = _byId.Values.FirstOrDefault(e =>
                    (e.Status == EscalationStatus.Open || e.Status == EscalationStatus.Acknowledged)
                    && string.Equals(e.SessionId, request.SessionId, StringComparison.Ordinal)
                    && string.Equals(e.AgentName, request.AgentName, StringComparison.Ordinal));
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "[HITL] Mükerrer eskalasyon engellendi — mevcut kayıt kullanılıyor. "
                      + "session={Session} agent={Agent} existingId={ExistingId}",
                        request.SessionId, request.AgentName, existing.Id);
                    return existing;
                }
            }

            _byId[request.Id] = request;
            _order.Enqueue(request.Id);
            Trim();
        }

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

    // Tek süreç: kayıtların tek kaynağı zaten bellektir, ayrı bir kalıcı depo yoktur.
    public Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EscalationRequest>>(_byId.Values
            .Where(e => string.IsNullOrEmpty(e.AssignedTo)
                     || string.Equals(e.AssignedTo, agentId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList());

    public IReadOnlyList<EscalationRequest> GetRecent(int count = 50) =>
        _byId.Values
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList();

    public EscalationRequest? Get(string id) =>
        _byId.TryGetValue(id, out var e) ? e : null;

    public Task<bool> DecideAsync(string id, string action, string? assignedTo = null, string? resolution = null)
        => Task.FromResult(Decide(id, action, assignedTo, resolution));

    private bool Decide(string id, string action, string? assignedTo, string? resolution)
    {
        EscalationRequest? decided;
        string normalized;
        lock (_gate)
            decided = TryTransitionLocked(id, action, assignedTo, resolution, out normalized);

        if (decided is null) return false;

        // Olay kilit DIŞINDA tetiklenir: handler'lar (SSE bildirimi vb.) sink'e geri çağrı
        // yapabilir ya da yavaş olabilir; kilidi tutarken çalıştırmak diğer kararları bekletirdi.
        _logger.LogInformation(
            "[HITL] Escalation decided: id={Id}, action={Action}, status={Status}",
            id, normalized, decided.Status);

        try { RequestDecided?.Invoke(this, decided); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

        return true;
    }

    private EscalationRequest? TryTransitionLocked(
        string id, string action, string? assignedTo, string? resolution, out string normalized)
    {
        // Aksiyon string'ini normalize et (altyapı katmanı sorumluluğu)
        normalized = (action ?? "").Trim().ToLowerInvariant();

        if (!_byId.TryGetValue(id, out var req)) return null;

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

        return success ? req : null;
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

