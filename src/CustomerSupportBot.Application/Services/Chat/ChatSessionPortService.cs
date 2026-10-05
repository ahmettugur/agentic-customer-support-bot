// Application/Services/ChatSessionPortService.cs
// DRIVING PORT IMPL — IChatSessionPort → canlı takeover ve replan orkestrasyonu.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

public sealed class ChatSessionPortService : IChatSessionPort
{
    private readonly IChatModeRegistry _chatModes;
    private readonly IChatBridge _chatBridge;
    private readonly ISessionManager _sessions;
    private readonly IEscalationSink _escalations;
    private readonly IHumanAgentRegistry _agents;
    private readonly IReplanService _replanService;

    public ChatSessionPortService(
        IChatModeRegistry chatModes,
        IChatBridge chatBridge,
        ISessionManager sessions,
        IEscalationSink escalations,
        IHumanAgentRegistry agents,
        IReplanService replanService)
    {
        _chatModes = chatModes;
        _chatBridge = chatBridge;
        _sessions = sessions;
        _escalations = escalations;
        _agents = agents;
        _replanService = replanService;
    }

    public IReadOnlyList<ChatSessionState> GetActive() => _chatModes.GetActive();

    public ChatSessionState GetStateOrDefault(string sessionId) =>
        _chatModes.GetState(sessionId) ?? new ChatSessionState
        {
            SessionId = sessionId,
            Mode = ChatMode.Bot
        };

    public Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50) =>
        _chatBridge.GetHistoryAsync(sessionId, take);

    public async Task<ChatSessionSentimentSnapshot?> GetSentimentAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct);
        if (session is null)
        {
            return null;
        }

        var state = session.State;
        return new ChatSessionSentimentSnapshot(
            state.Sentiment,
            state.SentimentScore,
            state.ConsecutiveNegativeTurns,
            state.SentimentHistory.TakeLast(10).ToList());
    }

    public Task PublishSystemMessageAsync(string sessionId, string text) =>
        _chatBridge.PublishSystemMessageAsync(sessionId, text);

    public async Task<ChatSessionTakeoverResult> TakeOverAsync(string sessionId, string humanAgent, string? agentId = null)
    {
        var ok = await _chatModes.TakeOverAsync(sessionId, humanAgent);
        if (!ok)
        {
            return new ChatSessionTakeoverResult(
                sessionId,
                humanAgent,
                0,
                ErrorCode: "takeover_failed",
                ErrorMessage: "TakeOver başarısız.");
        }

        await _chatBridge.PublishSystemMessageAsync(
            sessionId,
            $"Müşteri temsilcisi {humanAgent} sohbete katıldı.");

        var acknowledged = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId && escalation.Status == EscalationStatus.Open)
            {
                if (await _escalations.DecideAsync(
                    escalation.Id,
                    WellKnown.EscalationActions.Acknowledge,
                    assignedTo: agentId ?? humanAgent))
                {
                    acknowledged++;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            _agents.IncrementLoad(agentId);
        }

        return new ChatSessionTakeoverResult(sessionId, humanAgent, acknowledged);
    }

    public async Task<ChatSessionReleaseResult> ReleaseAsync(string sessionId, string? agentId = null)
    {
        var state = _chatModes.GetState(sessionId);
        var resolvedBy = string.IsNullOrWhiteSpace(agentId) ? state?.HumanAgent : agentId;

        var ok = await _chatModes.ReleaseAsync(sessionId);
        if (!ok)
        {
            return new ChatSessionReleaseResult(
                sessionId,
                0,
                ErrorCode: "not_found",
                ErrorMessage: "Session zaten Bot modda.");
        }

        await _chatBridge.PublishSystemMessageAsync(
            sessionId,
            "Müşteri temsilcisi sohbeti sonlandırdı. Bot moduna dönüldü.");

        var resolved = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId)
            {
                if (await _escalations.DecideAsync(
                    escalation.Id,
                    WellKnown.EscalationActions.Resolve,
                    assignedTo: resolvedBy,
                    resolution: WellKnown.FallbackMessages.LiveTakeoverResolution))
                {
                    resolved++;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            _agents.DecrementLoad(agentId);
        }

        return new ChatSessionReleaseResult(sessionId, resolved);
    }

    public async Task<ChatSessionMessageResult> SendAdminMessageAsync(
        string sessionId, string humanAgent, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ChatSessionMessageResult(
                sessionId,
                ErrorCode: "invalid_input",
                ErrorMessage: "text zorunlu.");
        }

        if (_chatModes.GetMode(sessionId) != ChatMode.Human)
        {
            return new ChatSessionMessageResult(
                sessionId,
                ErrorCode: "invalid_state",
                ErrorMessage: "Session Human modda değil.");
        }

        var trimmedText = text.Trim();
        await _chatBridge.PublishAdminMessageAsync(sessionId, humanAgent, trimmedText);
        var agentLabel = string.IsNullOrWhiteSpace(humanAgent) ? "Temsilci" : humanAgent;

        // ASISTAN rolü — kullanıcı rolü DEĞİL. İnsan modunda temsilci, konuşmada botun yerini
        // alır; söylediği şey müşteriden gelen bir GİRDİ değil, müşteriye verilen bir YANITTIR.
        // Kullanıcı rolüyle yazıldığında, bot oturumu geri devraldığında temsilcinin cevabını
        // müşterinin yeni bir mesajı sanıyor ve ona cevap vermeye çalışıyordu. Etiket korunur:
        // modelin bunu kendi ürettiği bir yanıt değil, insan temsilcinin sözü olarak görmesi
        // doğru bağlamı verir.
        await _sessions.AppendAssistantMessageAsync(sessionId, $"[🧑‍💼 {agentLabel}]: {trimmedText}", ct);
        return new ChatSessionMessageResult(sessionId);
    }

    public async Task<ChatSessionReplanResult> ReplanSessionAsync(
        string sessionId, string requestedBy, string? note, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct);
        if (session is null)
        {
            return new ChatSessionReplanResult(
                sessionId,
                null,
                requestedBy,
                0,
                false,
                ErrorCode: "not_found",
                ErrorMessage: "Session bulunamadı.");
        }

        // Mutasyon olarak yazılır: başka pod'un aynı anda yaptığı state değişikliğini ezmesin.
        await _sessions.MutateStateAsync(session.SessionId, state => ApplyReplanState(state, requestedBy, note), ct);

        var resolved = await ResolveOpenEscalationsForSessionAsync(sessionId, requestedBy, note);
        var releasedFromHuman = await ReleaseIfHumanModeAsync(sessionId);

        if (!string.IsNullOrWhiteSpace(note))
            await _chatBridge.PublishAdminOnlyMessageAsync(sessionId, $"📋 Temsilci yeniden planlama notu: \"{note}\"");
        await _chatBridge.PublishSystemMessageAsync(sessionId, WellKnown.FallbackMessages.ReplanCustomerNotice);
        _ = _replanService.ExecuteAsync(sessionId);

        return new ChatSessionReplanResult(
            sessionId,
            null,
            requestedBy,
            resolved,
            releasedFromHuman);
    }

    public async Task<ChatSessionReplanResult> ReplanEscalationAsync(
        string escalationId, string requestedBy, string? note, CancellationToken ct = default)
    {
        var escalation = _escalations.Get(escalationId);
        if (escalation is null)
        {
            return new ChatSessionReplanResult(
                string.Empty,
                escalationId,
                requestedBy,
                0,
                false,
                ErrorCode: "not_found",
                ErrorMessage: "Escalation bulunamıyor.");
        }

        if (string.IsNullOrEmpty(escalation.SessionId))
        {
            return new ChatSessionReplanResult(
                string.Empty,
                escalationId,
                requestedBy,
                0,
                false,
                ErrorCode: "invalid_request",
                ErrorMessage: "Eskalasyona bağlı bir session yok.");
        }

        var session = await _sessions.GetAsync(escalation.SessionId, ct);
        if (session is null)
        {
            return new ChatSessionReplanResult(
                escalation.SessionId,
                escalationId,
                requestedBy,
                0,
                false,
                ErrorCode: "not_found",
                ErrorMessage: "Session bulunamadı.");
        }

        // Mutasyon olarak yazılır: başka pod'un aynı anda yaptığı state değişikliğini ezmesin.
        await _sessions.MutateStateAsync(session.SessionId, state => ApplyReplanState(state, requestedBy, note), ct);

        var resolved = await _escalations.DecideAsync(
            escalationId,
            WellKnown.EscalationActions.Resolve,
            assignedTo: requestedBy,
            resolution: note ?? WellKnown.FallbackMessages.ReplanResolution)
            ? 1
            : 0;

        var releasedFromHuman = await ReleaseIfHumanModeAsync(escalation.SessionId);

        if (!string.IsNullOrWhiteSpace(note))
            await _chatBridge.PublishSystemMessageAsync(escalation.SessionId, $"📋 Temsilci yeniden planlama notu: \"{note}\"");
        await _chatBridge.PublishSystemMessageAsync(escalation.SessionId, WellKnown.FallbackMessages.ReplanCustomerNotice);
        _ = _replanService.ExecuteAsync(escalation.SessionId);

        return new ChatSessionReplanResult(
            escalation.SessionId,
            escalationId,
            requestedBy,
            resolved,
            releasedFromHuman);
    }

    public IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct) =>
        _chatBridge.SubscribeToAdminAsync(sessionId, ct);

    public IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct) =>
        _chatBridge.SubscribeToUserAsync(sessionId, ct);

    public IReadOnlyList<EscalationRequest> GetOpenEscalations() =>
        _escalations.GetOpen();

    public async Task<int> DismissOrphanedEscalationsAsync(string sessionId)
    {
        var count = 0;
        foreach (var esc in _escalations.GetOpen())
        {
            if (esc.SessionId != sessionId) continue;
            if (await _escalations.DecideAsync(esc.Id, WellKnown.EscalationActions.Dismiss,
                resolution: "Müşteri bağlantıyı kesti."))
                count++;
        }
        return count;
    }

    private static void ApplyReplanState(SessionState state, string requestedBy, string? note)
    {
        state.ForceReplanNextTurn = true;
        state.ReplanRequestedBy = requestedBy;
        state.ReplanRequestedAt = DateTime.UtcNow;
        state.ReplanNote = note;
    }

    private async Task<int> ResolveOpenEscalationsForSessionAsync(string sessionId, string requestedBy, string? note)
    {
        var resolved = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId)
            {
                if (await _escalations.DecideAsync(
                    escalation.Id,
                    WellKnown.EscalationActions.Resolve,
                    assignedTo: requestedBy,
                    resolution: note ?? WellKnown.FallbackMessages.ReplanResolution))
                {
                    resolved++;
                }
            }
        }

        return resolved;
    }

    private async Task<bool> ReleaseIfHumanModeAsync(string sessionId)
    {
        if (_chatModes.GetMode(sessionId) == ChatMode.Human)
        {
            return await _chatModes.ReleaseAsync(sessionId);
        }

        return false;
    }
}
