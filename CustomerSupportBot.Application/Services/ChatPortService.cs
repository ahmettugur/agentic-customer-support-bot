// Application/Services/ChatPortService.cs
// IChatPort implementasyonu.
// Driving adapter (HTTP endpoint) bu servis üzerinden Core'u çağırır.
// Non-streaming ve streaming her iki kullanım senaryosunu orkestre eder.

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// IChatPort implementasyonu — HTTP driving adapter'ının bağlandığı Application kapısı.
/// Driving adapter'lar (endpoints) input doğrulamayı (InputGuard) kendi katmanlarında
/// uygular; bu servis sanitized request alır ve use-case'i orkestre eder.
/// Human mode (HITL), sentiment güncelleme ve persist işlemleri burada yönetilir.
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
        var session = _sessions.GetOrCreate(request.SessionId);
        var sessionId = session.SessionId;
        var history = _sessions.GetHistory(sessionId);

        var reasoningResult = await _reasoning.ReasonAsync(query, session, history, ct);

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query);
        var response = await _team.RunAsync(query, history, session, reasoningResult);

        _sessionState.UpdateSessionIntent(session, reasoningResult.Intent);
        _sessions.AddExchange(sessionId, query, response);

        return new ChatResponse(response, sessionId, reasoningResult);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = request.Query;
        var session = _sessions.GetOrCreate(request.SessionId);
        var sessionId = session.SessionId;

        yield return new StreamEvent(StreamEventTypes.Session, new { sessionId });

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
                _sessions.AddExchange(sessionId, query, "");
                _chatBridge.PublishUserMessage(sessionId, query);
            }
            yield break;
        }

        var history = _sessions.GetHistory(sessionId);

        // Reasoning stream
        ReasoningResult? reasoningResult = null;
        await foreach (var evt in _reasoning.ReasonStreamingAsync(query, session, history, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
            {
                reasoningResult = rr;
                _sessionState.UpdateSessionIntent(session, rr.Intent);
                _sessionState.UpdateSessionSentiment(session, rr);
            }
        }

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query);

        // Workflow stream
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var evt in _team.RunStreamingAsync(query, history, session, reasoningResult, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is not null)
            {
                var text = TryExtractText(evt.Data);
                if (!string.IsNullOrEmpty(text))
                    responseBuilder.Append(text);
            }
        }

        var fullResponse = responseBuilder.ToString().TrimEnd();
        _sessionState.PersistExchange(sessionId, query, fullResponse, _chatBridge);

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

    private static string? TryExtractText(object data)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
                return textProp.GetString();
        }
        catch { /* ignore */ }
        return null;
    }
}
