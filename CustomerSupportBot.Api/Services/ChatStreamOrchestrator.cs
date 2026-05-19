// Services/ChatStreamOrchestrator.cs
// Orchestrates the SSE streaming chat flow including reasoning, workflow execution,
// and HITL (Human-in-the-Loop) event forwarding. Separates streaming concerns from HTTP layer.
// İş mantığı (sentiment, intent, persist) Application katmanındaki SessionStateService'e delege edilir.

namespace CustomerSupportBot.Api.Services;

using System.Text;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

/// <summary>
/// Orchestrates streaming chat interactions, handling reasoning streams, workflow execution,
/// and HITL event forwarding. Each method represents a distinct phase of the streaming pipeline.
/// </summary>
public sealed class ChatStreamOrchestrator
{
    private readonly IAgentTeamPort _team;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningPort _reasoningService;
    private readonly IApprovalQueue _approvalQueue;
    private readonly IEscalationSink _escalationSink;
    private readonly IChatModeRegistry _modeRegistry;
    private readonly IChatBridge _chatBridge;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly SessionStateService _sessionState;
    private readonly ILogger<ChatStreamOrchestrator> _logger;

    public ChatStreamOrchestrator(
        IAgentTeamPort team,
        ISessionManager sessionManager,
        IReasoningPort reasoningService,
        IApprovalQueue approvalQueue,
        IEscalationSink escalationSink,
        IChatModeRegistry modeRegistry,
        IChatBridge chatBridge,
        IApprovalContextAccessor approvalContext,
        SessionStateService sessionState,
        ILogger<ChatStreamOrchestrator> logger)
    {
        _team = team;
        _sessionManager = sessionManager;
        _reasoningService = reasoningService;
        _approvalQueue = approvalQueue;
        _escalationSink = escalationSink;
        _modeRegistry = modeRegistry;
        _chatBridge = chatBridge;
        _approvalContext = approvalContext;
        _sessionState = sessionState;
        _logger = logger;
    }

    /// <summary>
    /// Executes the streaming chat flow for a single user request.
    /// This is the main entry point that coordinates all phases of the streaming pipeline.
    /// </summary>
    public async Task ExecuteAsync(
        ChatRequest request,
        AgentSession session,
        SseForwarder sse,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;

        // Phase 1: Check for Human Mode (HITL Live Takeover)
        if (_modeRegistry.GetMode(sessionId) == ChatMode.Human)
        {
            await HandleHumanModeAsync(request, session, sse, ct);
            return;
        }

        // Phase 2: Subscribe to HITL events and set up forwarding
        using var hitlSubscription = new HitlStreamSubscription(_approvalQueue, _escalationSink, sse, sessionId);
        hitlSubscription.Subscribe();

        await ExecuteBotModeAsync(request, session, sse, ct);
    }

    /// <summary>
    /// Handles the Human Mode flow where bot is bypassed and admin takes over.
    /// </summary>
    private async Task HandleHumanModeAsync(
        ChatRequest request,
        AgentSession session,
        SseForwarder sse,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;
        var state = _modeRegistry.GetState(sessionId);

        await sse.WriteAsync(StreamEventTypes.HumanJoined, new
        {
            sessionId,
            humanAgent = state?.HumanAgent ?? WellKnown.Defaults.Admin,
            enteredAt = state?.EnteredAt
        });

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            // Record in session history with empty assistant response (bot didn't run)
            _sessionManager.AddExchange(sessionId, request.Query, "");
            _chatBridge.PublishUserMessage(sessionId, request.Query);
        }

        await sse.WriteDoneAsync(sessionId);
    }

    /// <summary>
    /// Executes the normal bot-mode flow: reasoning � workflow � response.
    /// </summary>
    private async Task ExecuteBotModeAsync(
        ChatRequest request,
        AgentSession session,
        SseForwarder sse,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;

        // Load conversation history once for both reasoning and workflow
        var history = _sessionManager.GetHistory(sessionId);

        // Phase 1: Reasoning Stream
        var finalReasoning = await ExecuteReasoningStreamAsync(
            request.Query, session, history, sse, ct);

        // Set approval context for tools � IDisposable scope ensures cleanup after workflow
        using var approvalScope = _approvalContext.SetScope(sessionId, null, request.Query);

        // Phase 2: LLM sentiment → session state override (Application katmanına delege)
        if (finalReasoning != null)
        {
            _sessionState.UpdateSessionSentiment(session, finalReasoning);
        }

        // Phase 3: Workflow Stream
        var fullResponse = await ExecuteWorkflowStreamAsync(
            request.Query, history, session, finalReasoning, sse, ct);

        // Phase 4: Persist conversation (Application katmanına delege)
        _sessionState.PersistExchange(sessionId, request.Query, fullResponse, _chatBridge);

        // Phase 5: Sentiment SSE events → her tur sonrası frontend'e gönder
        var updatedSession = _sessionManager.Get(sessionId);
        if (updatedSession != null)
        {
            await EmitSentimentEventsAsync(updatedSession, sse);
        }

        await sse.WriteDoneAsync(sessionId);
    }

    /// <summary>
    /// Streams the reasoning phase and extracts the final reasoning result.
    /// Also updates session intent state if reasoning succeeds.
    /// </summary>
    private async Task<ReasoningResult?> ExecuteReasoningStreamAsync(
        string query,
        AgentSession session,
        List<ChatMessage> history,
        SseForwarder sse,
        CancellationToken ct)
    {
        ReasoningResult? finalReasoning = null;

        await foreach (var evt in _reasoningService.ReasonStreamingAsync(query, session, history, ct))
        {
            await sse.WriteAsync(evt.Type, evt.Data);

            if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
            {
                finalReasoning = rr;
                _sessionState.UpdateSessionIntent(session, rr.Intent);
            }
        }

        return finalReasoning;
    }

    /// <summary>
    /// Executes the workflow stream and accumulates the full response text.
    /// </summary>
    private async Task<string> ExecuteWorkflowStreamAsync(
        string query,
        List<ChatMessage> history,
        AgentSession session,
        ReasoningResult? reasoning,
        SseForwarder sse,
        CancellationToken ct)
    {
        var responseBuilder = new StringBuilder();

        await foreach (var evt in _team.RunStreamingAsync(query, history, session, reasoning, ct))
        {
            await sse.WriteAsync(evt.Type, evt.Data);

            if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is not null)
            {
                var text = Infrastructure.SseWriter.GetTextFromAnon(evt.Data);
                if (!string.IsNullOrEmpty(text))
                {
                    responseBuilder.Append(text);
                }
            }
        }

        return responseBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// Her tur sonrası sentiment_update SSE event'i yayınlar.
    /// Ardışık negatif sayacı eşiği aşarsa sentiment_alert de gönderir.
    /// Sentiment iş mantığı SessionStateService'te, burada sadece SSE transport.
    /// </summary>
    private async Task EmitSentimentEventsAsync(AgentSession session, SseForwarder sse)
    {
        var alert = _sessionState.CheckSentimentAlert(session);

        // Her tur: sentiment_update
        await sse.WriteAsync(StreamEventTypes.SentimentUpdate, new
        {
            sentiment = alert.Sentiment,
            score = alert.Score,
            consecutive = alert.ConsecutiveNegativeTurns,
            sessionId = alert.SessionId
        });

        // Otomatik eskalasyon uyarısı — ardışık negatif eşik aşıldıysa
        if (alert.ShouldAlert)
        {
            await sse.WriteAsync(StreamEventTypes.SentimentAlert, new
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
