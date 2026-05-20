// Services/ChatEventOrchestrator.cs
// Manages the persistent SSE connection for real-time chat events including
// Human-in-the-Loop mode changes, escalation lifecycle, and admin messages.

namespace CustomerSupportBot.Api.Services;

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Orchestrates persistent SSE events for chat sessions. Handles HITL mode transitions,
/// escalation lifecycle events, and admin message streaming. This connection remains
/// open for the lifetime of the chat session (per-session, long-lived).
/// </summary>
public sealed class ChatEventOrchestrator(
    IChatSessionPort chatSession,
    IHitlEventPort hitlEvents,
    IHostApplicationLifetime appLifetime,
    ILogger<ChatEventOrchestrator> logger)
{
    /// <summary>
    /// Executes the persistent event stream for a chat session.
    /// Returns when the client disconnects or cancellation is requested.
    /// </summary>
    public async Task ExecuteAsync(string sessionId, SseForwarder sse, CancellationToken ct)
    {
        await sse.WriteSessionAsync(sessionId);
        await SendInitialStateAsync(sessionId, sse);

        using var subscription = hitlEvents.SubscribeToChatEvents(
            sessionId,
            (eventType, data) => sse.WriteAsync(eventType, data));

        await ProcessBridgeMessagesAsync(sessionId, sse, ct);

        if (!appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            var dismissed = chatSession.DismissOrphanedEscalations(sessionId);
            if (dismissed > 0)
                logger.LogInformation(
                    "[Escalation] Müşteri ayrıldı, {Count} eskalasyon otomatik kapatıldı. session={Session}",
                    dismissed, sessionId);
        }
    }

    private async Task SendInitialStateAsync(string sessionId, SseForwarder sse)
    {
        var state = chatSession.GetStateOrDefault(sessionId);

        if (state.Mode == ChatMode.Human)
        {
            await sse.WriteAsync(StreamEventTypes.HumanJoined, new
            {
                sessionId,
                humanAgent = state.HumanAgent ?? WellKnown.Defaults.Admin,
                enteredAt = state.EnteredAt
            });
        }
        else
        {
            var pending = chatSession.GetOpenEscalations()
                .FirstOrDefault(e => e.SessionId == sessionId);

            if (pending != null)
            {
                await sse.WriteAsync(StreamEventTypes.HandoffPending, new
                {
                    escalationId = pending.Id,
                    reason = pending.Reason,
                    createdAt = pending.CreatedAt
                });
            }
        }
    }

    private async Task ProcessBridgeMessagesAsync(string sessionId, SseForwarder sse, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in chatSession.SubscribeToUserAsync(sessionId, ct))
            {
                if (msg.Sender == ChatBridgeSender.BotTyping)
                {
                    await sse.WriteAsync(StreamEventTypes.BotTyping, new
                    {
                        sessionId,
                        on = string.Equals(msg.Text, "on", StringComparison.OrdinalIgnoreCase)
                    });
                    continue;
                }

                await sse.WriteAsync(StreamEventTypes.HumanMessage, new
                {
                    id = msg.Id,
                    from = msg.Sender.ToString().ToLowerInvariant(),
                    humanAgent = msg.HumanAgent,
                    text = msg.Text,
                    timestamp = msg.Timestamp
                });
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("SSE connection closed for session {SessionId}", sessionId);
        }
    }
}
