// Application/Services/ChatSessionPortService.cs
// DRIVING PORT IMPL — IChatSessionPort → canlı takeover ve replan orkestrasyonu.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

public sealed class ChatSessionPortService : IChatSessionPort
{
    private readonly IChatModeRegistry _chatModes;
    private readonly IChatBridge _chatBridge;
    private readonly ISessionManager _sessions;
    private readonly IEscalationSink _escalations;
    private readonly IHumanAgentRegistry _agents;
    private readonly IReplanPort _replanPort;

    public ChatSessionPortService(
        IChatModeRegistry chatModes,
        IChatBridge chatBridge,
        ISessionManager sessions,
        IEscalationSink escalations,
        IHumanAgentRegistry agents,
        IReplanPort replanPort)
    {
        _chatModes = chatModes;
        _chatBridge = chatBridge;
        _sessions = sessions;
        _escalations = escalations;
        _agents = agents;
        _replanPort = replanPort;
    }

    public IReadOnlyList<ChatSessionState> GetActive() => _chatModes.GetActive();

    public ChatSessionState GetStateOrDefault(string sessionId) =>
        _chatModes.GetState(sessionId) ?? new ChatSessionState
        {
            SessionId = sessionId,
            Mode = ChatMode.Bot
        };

    public IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50) =>
        _chatBridge.GetHistory(sessionId, take);

    public ChatSessionSentimentSnapshot? GetSentiment(string sessionId)
    {
        var session = _sessions.Get(sessionId);
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

    public void PublishSystemMessage(string sessionId, string text) =>
        _chatBridge.PublishSystemMessage(sessionId, text);

    public ChatSessionTakeoverResult TakeOver(string sessionId, string humanAgent, string? agentId = null)
    {
        var ok = _chatModes.TakeOver(sessionId, humanAgent);
        if (!ok)
        {
            return new ChatSessionTakeoverResult(
                sessionId,
                humanAgent,
                0,
                ErrorCode: "takeover_failed",
                ErrorMessage: "TakeOver başarısız.");
        }

        _chatBridge.PublishSystemMessage(
            sessionId,
            $"Müşteri temsilcisi {humanAgent} sohbete katıldı.");

        var acknowledged = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId && escalation.Status == EscalationStatus.Open)
            {
                if (_escalations.Decide(
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

    public ChatSessionReleaseResult Release(string sessionId, string? agentId = null)
    {
        var state = _chatModes.GetState(sessionId);
        var resolvedBy = string.IsNullOrWhiteSpace(agentId) ? state?.HumanAgent : agentId;

        var ok = _chatModes.Release(sessionId);
        if (!ok)
        {
            return new ChatSessionReleaseResult(
                sessionId,
                0,
                ErrorCode: "not_found",
                ErrorMessage: "Session zaten Bot modda.");
        }

        _chatBridge.PublishSystemMessage(
            sessionId,
            "Müşteri temsilcisi sohbeti sonlandırdı. Bot moduna dönüldü.");

        var resolved = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId)
            {
                if (_escalations.Decide(
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

    public ChatSessionMessageResult SendAdminMessage(string sessionId, string humanAgent, string text)
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
        _chatBridge.PublishAdminMessage(sessionId, humanAgent, trimmedText);
        _sessions.AppendAssistantMessage(sessionId, trimmedText);
        return new ChatSessionMessageResult(sessionId);
    }

    public ChatSessionReplanResult ReplanSession(string sessionId, string requestedBy, string? note)
    {
        var session = _sessions.Get(sessionId);
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

        ApplyReplanState(session, requestedBy, note);
        _sessions.Update(session);

        var resolved = ResolveOpenEscalationsForSession(sessionId, requestedBy, note);
        var releasedFromHuman = ReleaseIfHumanMode(sessionId);

        _chatBridge.PublishSystemMessage(sessionId, WellKnown.FallbackMessages.ReplanCustomerNotice);
        _ = _replanPort.ExecuteAsync(sessionId);

        return new ChatSessionReplanResult(
            sessionId,
            null,
            requestedBy,
            resolved,
            releasedFromHuman);
    }

    public ChatSessionReplanResult ReplanEscalation(string escalationId, string requestedBy, string? note)
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

        var session = _sessions.Get(escalation.SessionId);
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

        ApplyReplanState(session, requestedBy, note);
        _sessions.Update(session);

        var resolved = _escalations.Decide(
            escalationId,
            WellKnown.EscalationActions.Resolve,
            assignedTo: requestedBy,
            resolution: note ?? WellKnown.FallbackMessages.ReplanResolution)
            ? 1
            : 0;

        var releasedFromHuman = ReleaseIfHumanMode(escalation.SessionId);

        _chatBridge.PublishSystemMessage(escalation.SessionId, WellKnown.FallbackMessages.ReplanCustomerNotice);
        _ = _replanPort.ExecuteAsync(escalation.SessionId);

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

    public int DismissOrphanedEscalations(string sessionId)
    {
        var count = 0;
        foreach (var esc in _escalations.GetOpen())
        {
            if (esc.SessionId != sessionId) continue;
            if (_escalations.Decide(esc.Id, WellKnown.EscalationActions.Dismiss,
                resolution: "Müşteri bağlantıyı kesti."))
                count++;
        }
        return count;
    }

    private static void ApplyReplanState(AgentSession session, string requestedBy, string? note)
    {
        session.State.ForceReplanNextTurn = true;
        session.State.ReplanRequestedBy = requestedBy;
        session.State.ReplanRequestedAt = DateTime.UtcNow;
        session.State.ReplanNote = note;
    }

    private int ResolveOpenEscalationsForSession(string sessionId, string requestedBy, string? note)
    {
        var resolved = 0;
        foreach (var escalation in _escalations.GetOpen())
        {
            if (escalation.SessionId == sessionId)
            {
                if (_escalations.Decide(
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

    private bool ReleaseIfHumanMode(string sessionId)
    {
        if (_chatModes.GetMode(sessionId) == ChatMode.Human)
        {
            return _chatModes.Release(sessionId);
        }

        return false;
    }
}
