// Services/ChatEventOrchestrator.cs
// Manages the persistent SSE connection for real-time chat events including
// Human-in-the-Loop mode changes, escalation lifecycle, and admin messages.

namespace CustomerSupportBot.Api.Services;

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Orchestrates persistent SSE events for chat sessions. Handles HITL mode transitions,
/// escalation lifecycle events, and admin message streaming. This connection remains
/// open for the lifetime of the chat session (per-session, long-lived).
/// </summary>
public sealed class ChatEventOrchestrator
{
    private readonly IChatModeRegistry _modeRegistry;
    private readonly IChatBridge _chatBridge;
    private readonly IEscalationSink _escalationSink;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<ChatEventOrchestrator> _logger;

    public ChatEventOrchestrator(
        IChatModeRegistry modeRegistry,
        IChatBridge chatBridge,
        IEscalationSink escalationSink,
        IHostApplicationLifetime appLifetime,
        ILogger<ChatEventOrchestrator> logger)
    {
        _modeRegistry = modeRegistry;
        _chatBridge = chatBridge;
        _escalationSink = escalationSink;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Executes the persistent event stream for a chat session.
    /// Returns when the client disconnects or cancellation is requested.
    /// </summary>
    public async Task ExecuteAsync(string sessionId, SseForwarder sse, CancellationToken ct)
    {
        // Write initial session event
        await sse.WriteSessionAsync(sessionId);

        // Send current state snapshot (human mode or pending escalation)
        await SendInitialStateAsync(sessionId, sse);

        // Set up event subscriptions
        using var subscription = new ChatEventSubscription(
            sessionId, _modeRegistry, _escalationSink, sse);
        subscription.Subscribe();

        // Main loop: forward admin messages from chat bridge
        await ProcessBridgeMessagesAsync(sessionId, sse, ct);

        // Bağlantı kapandı - kaynağı kontrol et:
        // Server shutdown ise eskalasyonlara dokunma (restart sonrası hayatta kalsın).
        // Sadece müşteri kendi istediyle ayrıldıysa otomatik kapat.
        if (!_appLifetime.ApplicationStopping.IsCancellationRequested)
            DismissOrphanedEscalations(sessionId);
    }

    private void DismissOrphanedEscalations(string sessionId)
    {
        try
        {
            foreach (var esc in _escalationSink.GetOpen())
            {
                if (esc.SessionId != sessionId) continue;
                var dismissed = _escalationSink.Decide(
                    esc.Id,
                    WellKnown.EscalationActions.Dismiss,
                    resolution: "Müşteri bağlantıyı kesti.");
                if (dismissed)
                    _logger.LogInformation(
                        "[Escalation] Müşteri ayrıldı, eskalasyon otomatik kapatıldı: {Id} session={Session}",
                        esc.Id, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Escalation] Auto-dismiss başarısız. session={Session}", sessionId);
        }
    }

    /// <summary>
    /// Sends the initial state snapshot based on current session mode or pending escalations.
    /// This ensures UI consistency on page refresh.
    /// </summary>
    private async Task SendInitialStateAsync(string sessionId, SseForwarder sse)
    {
        var state = _modeRegistry.GetState(sessionId);

        if (state?.Mode == ChatMode.Human)
        {
            // Already in human mode, send joined event immediately
            await sse.WriteAsync(StreamEventTypes.HumanJoined, new
            {
                sessionId,
                humanAgent = state.HumanAgent ?? WellKnown.Defaults.Admin,
                enteredAt = state.EnteredAt
            });
        }
        else
        {
            // Check for pending escalation
            var pending = _escalationSink.GetOpen()
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

    /// <summary>
    /// Processes messages from the chat bridge (admin/system - user).
    /// Continues until cancellation is requested.
    /// </summary>
    private async Task ProcessBridgeMessagesAsync(string sessionId, SseForwarder sse, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _chatBridge.SubscribeToUserAsync(sessionId, ct))
            {
                if (msg.Sender == ChatBridgeSender.BotTyping)
                {
                    // Transient kontrol sinyali - typing indicator aç/kapa
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
            // Client disconnected - expected behavior
            _logger.LogDebug("SSE connection closed for session {SessionId}", sessionId);
        }
    }

}
