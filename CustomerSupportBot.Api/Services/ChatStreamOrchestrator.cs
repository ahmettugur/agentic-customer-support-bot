// Services/ChatStreamOrchestrator.cs
// Orchestrates the SSE streaming chat flow including reasoning, workflow execution,
// and HITL (Human-in-the-Loop) event forwarding. Separates streaming concerns from HTTP layer.

namespace CustomerSupportBot.Services;

using System.Text;
using CustomerSupportBot.Agents;
using CustomerSupportBot.Infrastructure;
using CustomerSupportBot.Models;
using Microsoft.Extensions.AI;

/// <summary>
/// Orchestrates streaming chat interactions, handling reasoning streams, workflow execution,
/// and HITL event forwarding. Each method represents a distinct phase of the streaming pipeline.
/// </summary>
public sealed class ChatStreamOrchestrator
{
    private readonly CustomerSupportTeam _team;
    private readonly ISessionManager _sessionManager;
    private readonly ReasoningService _reasoningService;
    private readonly IApprovalQueue _approvalQueue;
    private readonly IEscalationSink _escalationSink;
    private readonly IChatModeRegistry _modeRegistry;
    private readonly IChatBridge _chatBridge;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly ILogger<ChatStreamOrchestrator> _logger;

    public ChatStreamOrchestrator(
        CustomerSupportTeam team,
        ISessionManager sessionManager,
        ReasoningService reasoningService,
        IApprovalQueue approvalQueue,
        IEscalationSink escalationSink,
        IChatModeRegistry modeRegistry,
        IChatBridge chatBridge,
        IApprovalContextAccessor approvalContext,
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
    /// Executes the normal bot-mode flow: reasoning → workflow → response.
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

        // Set approval context for tools — IDisposable scope ensures cleanup after workflow
        using var approvalScope = _approvalContext.SetScope(sessionId, null, request.Query);

        // Phase 2: LLM sentiment → session state override (kural tabanlı üzerine yazar)
        // Not: Ayrı lock almaz — hemen ardından AddExchange lock altında persist eder.
        if (finalReasoning != null)
        {
            UpdateSessionSentiment(session, finalReasoning);
        }

        // Phase 3: Workflow Stream
        var fullResponse = await ExecuteWorkflowStreamAsync(
            request.Query, history, session, finalReasoning, sse, ct);

        // Phase 4: Persist conversation (state extraction triggers rule-based sentiment too)
        if (!string.IsNullOrWhiteSpace(fullResponse))
        {
            _sessionManager.AddExchange(sessionId, request.Query, fullResponse);
            _chatBridge.RecordBotExchange(sessionId, request.Query, fullResponse);
        }

        // Phase 5: Sentiment SSE events — her tur sonrası frontend'e gönder
        var updatedSession = _sessionManager.GetSession(sessionId);
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
                UpdateSessionIntent(session, rr.Intent);
            }
        }

        return finalReasoning;
    }

    /// <summary>
    /// Updates the session intent if the reasoning provided a valid intent.
    /// </summary>
    private void UpdateSessionIntent(AgentSession session, string? intent)
    {
        if (!string.IsNullOrWhiteSpace(intent) && intent != WellKnown.Intents.Unknown)
        {
            session.State.CurrentIntent = intent;
            _sessionManager.UpdateSession(session);
        }
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
                var text = Endpoints.SseWriter.GetTextFromAnon(evt.Data);
                if (!string.IsNullOrEmpty(text))
                {
                    responseBuilder.Append(text);
                }
            }
        }

        return responseBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// LLM reasoning sonucundaki sentiment'i session state'e yazar.
    /// Kural tabanlı sonucu override eder (LLM daha doğru).
    /// Not: Ayrı distributed lock almaz — AddExchange hemen ardından zaten lock altında
    /// state persist eder. Burada sadece in-memory session objesini güncelliyoruz.
    /// </summary>
    private void UpdateSessionSentiment(AgentSession session, ReasoningResult reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning.Sentiment) ||
            reasoning.Sentiment == WellKnown.Sentiments.Neutral && reasoning.SentimentScore == 0.5)
        {
            return; // LLM sentiment döndürmemiş, kural tabanlı sonucu koru
        }

        var state = session.State;
        state.Sentiment = reasoning.Sentiment;
        state.SentimentScore = reasoning.SentimentScore;

        // Ardışık negatif sayacını güncelle
        if (reasoning.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
            state.ConsecutiveNegativeTurns++;
        else
            state.ConsecutiveNegativeTurns = 0;
    }

    /// <summary>
    /// Her tur sonrası sentiment_update SSE event'i yayınlar.
    /// Ardışık negatif sayacı eşiği aşarsa sentiment_alert de gönderir.
    /// </summary>
    private async Task EmitSentimentEventsAsync(AgentSession session, SseForwarder sse)
    {
        var state = session.State;

        // Her tur: sentiment_update
        await sse.WriteAsync(StreamEventTypes.SentimentUpdate, new
        {
            sentiment = state.Sentiment,
            score = state.SentimentScore,
            consecutive = state.ConsecutiveNegativeTurns,
            sessionId = session.SessionId
        });

        // Otomatik eskalasyon uyarısı — ardışık negatif eşik aşıldıysa
        if (state.ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative)
        {
            _logger.LogWarning(
                "[Sentiment Alert] Session {SessionId}: {Consecutive} ardışık negatif tur (skor: {Score})",
                session.SessionId, state.ConsecutiveNegativeTurns, state.SentimentScore);

            await sse.WriteAsync(StreamEventTypes.SentimentAlert, new
            {
                sentiment = state.Sentiment,
                score = state.SentimentScore,
                consecutive = state.ConsecutiveNegativeTurns,
                sessionId = session.SessionId,
                message = $"Müşteri {state.ConsecutiveNegativeTurns} tur boyunca olumsuz. Bir temsilci bağlanmalı."
            });
        }
    }

}
