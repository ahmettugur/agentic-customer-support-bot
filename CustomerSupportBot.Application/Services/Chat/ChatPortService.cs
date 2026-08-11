using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// IChatPort implementasyonu — chat kullanım senaryosunu orkestre eder.
///Human mode (HITL), sentiment güncelleme ve persist işlemleri burada yönetilir.
/// </summary>
public sealed class ChatPortService : IChatPort
{
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoning;
    private readonly ISessionManager _sessions;
    private readonly IChatModeRegistry _modeRepo;
    private readonly IChatBridge _chatBridge;
    private readonly SessionStateService _sessionState;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly ILogger<ChatPortService> _logger;

    public ChatPortService(
        IAgentTeamPort team,
        IReasoningPort reasoning,
        ISessionManager sessions,
        IChatModeRegistry modeRepo,
        IChatBridge chatBridge,
        SessionStateService sessionState,
        IApprovalContextAccessor approvalContext,
        ILogger<ChatPortService> logger)
    {
        _team = team;
        _reasoning = reasoning;
        _sessions = sessions;
        _modeRepo = modeRepo;
        _chatBridge = chatBridge;
        _sessionState = sessionState;
        _approvalContext = approvalContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default)
    {
        var query = request.Query;
        var session = await _sessions.GetOrCreateAsync(request.SessionId, ct);
        var sessionId = session.SessionId;
        await BindAuthenticatedCustomerAsync(session, request.CustomerId, ct);
        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        var reasoningResult = await _reasoning.ReasonAsync(query, session, history, ct);

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query, session.State.AuthenticatedCustomerId);
        var response = await _team.RunAsync(query, history, session, reasoningResult, ct);

        // Intent/sentiment turun kapanışında TEK yerde işlenir — bkz. TurnSignals.
        // (Bu yol eskiden sentiment'i hiç iletmiyordu; streaming yolla arasındaki
        // davranış farkı da böylece kapanıyor.)
        await _sessions.AddExchangeAsync(sessionId, query, response, TurnSignals.From(reasoningResult), ct);

        return new ChatResponse(response, sessionId, reasoningResult);
    }

    /// <summary>
    /// Login'li müşterinin JWT'den doğrulanmış kimliğini session'a bir kez bağlar — bir sonraki
    /// turlarda tekrar yazılmaz (session zaten bağlıysa no-op), böylece onay gerektiren tool'lar
    /// için güvenilir tek kaynak kalıcı olur.
    /// </summary>
    private async Task BindAuthenticatedCustomerAsync(AgentSession session, string? customerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId) || session.State.AuthenticatedCustomerId is not null)
            return;

        session.State.AuthenticatedCustomerId = customerId;
        await _sessions.UpdateAsync(session, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = request.Query;
        var session = await _sessions.GetOrCreateAsync(request.SessionId, ct);
        var sessionId = session.SessionId;
        await BindAuthenticatedCustomerAsync(session, request.CustomerId, ct);

        yield return new StreamEvent(StreamEventTypes.Session, new SessionEventPayload(sessionId));

        // Human mode (HITL live takeover) — bot bypass
        if (_modeRepo.GetMode(sessionId) == ChatMode.Human)
        {
            var state = _modeRepo.GetState(sessionId);
            yield return new StreamEvent(StreamEventTypes.HumanJoined, new
            {
                sessionId,
                humanAgent = state?.HumanAgent ?? WellKnown.Defaults.Admin,
                enteredAt = state?.EnteredAt
            });
            if (!string.IsNullOrWhiteSpace(query))
            {
                await _sessions.AddExchangeAsync(sessionId, query, "", ct: ct);
                _chatBridge.PublishUserMessage(sessionId, query);
            }
            yield break;
        }

        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        // Reasoning stream
        ReasoningResult? reasoningResult = null;
        await foreach (var evt in _reasoning.ReasonStreamingAsync(query, session, history, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
            {
                reasoningResult = rr;
            }
        }

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query, session.State.AuthenticatedCustomerId);

        // Workflow stream
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var evt in _team.RunStreamingAsync(query, history, session, reasoningResult, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is TextDeltaPayload { Text.Length: > 0 } delta)
            {
                responseBuilder.Append(delta.Text);
            }
        }

        var fullResponse = responseBuilder.ToString().TrimEnd();
        await _sessionState.PersistExchangeAsync(
            sessionId, query, fullResponse, _chatBridge, TurnSignals.From(reasoningResult), ct);

        // Sentiment events
        var alert = _sessionState.CheckSentimentAlert(session);
        yield return new StreamEvent(StreamEventTypes.SentimentUpdate, new
        {
            sentiment = alert.Sentiment,
            score = alert.Score,
            consecutive = alert.ConsecutiveNegativeTurns,
            sessionId = alert.SessionId
        });
        if (alert.ShouldAlert)
        {
            yield return new StreamEvent(StreamEventTypes.SentimentAlert, new
            {
                sentiment = alert.Sentiment,
                score = alert.Score,
                consecutive = alert.ConsecutiveNegativeTurns,
                sessionId = alert.SessionId,
                message = alert.AlertMessage
            });
        }
    }

}
