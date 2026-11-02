// Application/Services/EscalationPortService.cs
// DRIVING PORT IMPL — IEscalationPort → Eskalasyon yönetimi orkestrasyonu.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Escalation;

/// <summary>
/// Eskalasyon yönetimi driving port implementasyonu.
/// Admin panel (AdminEndpoints) bu sınıfı IEscalationPort olarak kullanır.
/// </summary>
public sealed class EscalationPortService : IEscalationPort
{
    private readonly IEscalationSink _escalations;
    private readonly ILogger<EscalationPortService> _logger;

    public event EventHandler<EscalationRequest>? RequestCreated;
    public event EventHandler<EscalationRequest>? RequestDecided;

    /// <summary>
    /// Eskalasyon turun sonunda oluşur; mesajlar kayıttan birkaç saniye sonraya kadar yazılabilir (ölçüldü: en çok 2 sn). Pencere bu turu
    /// kapsar, sonraki turlara uzanmaz.
    /// </summary>
    private static readonly TimeSpan TurnWindow = TimeSpan.FromSeconds(5);

    private readonly IConversationExcerptStore? _excerpts;

    public EscalationPortService(
        IEscalationSink escalations,
        ILogger<EscalationPortService> logger,
        IConversationExcerptStore? excerpts = null)
    {
        _excerpts = excerpts;
        _escalations = escalations;
        _logger = logger;

        // Driven port event'lerini driving port'a bridge et
        _escalations.RequestCreated += (_, req) => RequestCreated?.Invoke(this, req);
        _escalations.RequestDecided += (_, req) => RequestDecided?.Invoke(this, req);
    }

    public async Task<EscalationRequest> CreateAsync(EscalationRequest request)
    {
        var created = await _escalations.CreateAsync(request);
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

    public Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default)
    {
        // Filtreleme burada YAPILMAZ; adaptöre devredilir. Cache üzerinde elemek, cache'in
        // kendisi bir "son N" penceresi olduğu için sınırı ötelemekten ibarettir.
        return _escalations.GetRecentForAgentAsync(agentId, count, ct);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ConversationExcerptLine>>> GetConversationsAsync(
        IReadOnlyList<EscalationRequest> escalations, CancellationToken ct = default)
    {
        var map = new Dictionary<string, IReadOnlyList<ConversationExcerptLine>>(escalations.Count, StringComparer.Ordinal);
        foreach (var escalation in escalations)
        {
            IReadOnlyList<ConversationMessage> before = _excerpts is null || string.IsNullOrEmpty(escalation.SessionId)
                ? []
                : await _excerpts.GetBeforeAsync(
                    escalation.SessionId, escalation.CreatedAt + TurnWindow, ConversationExcerpt.MaxMessages * 2, ct);

            map[escalation.Id] = ConversationExcerpt.Build(before, escalation.UserQuery, turnMayBeAnswered: true);
        }
        return map;
    }

    public EscalationRequest? Get(string id)
    {
        return _escalations.Get(id);
    }

    public async Task<bool> DecideAsync(string id, string action, string? assignedTo = null, string? resolution = null)
    {
        var request = _escalations.Get(id);
        if (request is null)
        {
            _logger.LogWarning("Escalation not found: {Id}", id);
            return false;
        }

        var result = await _escalations.DecideAsync(id, action, assignedTo, resolution);

        if (result)
        {
            _logger.LogInformation(
                "Escalation decision applied: {Id} action={Action} assignedTo={AssignedTo}",
                id, action, assignedTo ?? "unassigned");
        }

        return result;
    }
}
