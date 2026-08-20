// Services/ChatEventOrchestrator.cs
// Manages the persistent SSE connection for real-time chat events including
// Human-in-the-Loop mode changes, escalation lifecycle, and admin messages.

namespace CustomerSupportBot.Api.Services;

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
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
    /// <param name="stillAuthorized">
    /// Akışın HÂLÂ bu aboneye ait olup olmadığını söyleyen kontrol. Bağlantı açılışındaki tek
    /// seferlik kontrol yetmez: henüz kimseye bağlı OLMAYAN bir oturuma abone olmak serbesttir
    /// (ilk temasın oturumu çağırana bağlaması için), ama oturum daha sonra BAŞKA bir müşteriye
    /// bağlanabilir. Açık akış yeniden yetkilendirilmezse o müşterinin bot yanıtları, temsilci
    /// mesajları ve onay sonuçları ilk aboneye akmaya devam ederdi. Bu yüzden kontrol her olay
    /// yazımından önce tekrarlanır ve sahiplik değiştiği anda akış kapatılır.
    /// </param>
    public async Task ExecuteAsync(
        string sessionId,
        SseForwarder sse,
        Func<CancellationToken, Task<bool>> stillAuthorized,
        CancellationToken ct)
    {
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task GuardedWriteAsync(string eventType, object? data)
        {
            if (streamCts.IsCancellationRequested) return;

            if (!await stillAuthorized(streamCts.Token).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "[SSE] Oturum sahipliği değişti — akış kapatılıyor. session={Session} event={Event}",
                    sessionId, eventType);
                await streamCts.CancelAsync().ConfigureAwait(false);
                return;
            }

            await sse.WriteAsync(eventType, data).ConfigureAwait(false);
        }

        await sse.WriteSessionAsync(sessionId);
        await SendInitialStateAsync(sessionId, GuardedWriteAsync);

        using var subscription = hitlEvents.SubscribeToChatEvents(
            sessionId,
            GuardedWriteAsync);

        await ProcessBridgeMessagesAsync(sessionId, GuardedWriteAsync, streamCts.Token);

        if (!appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            var dismissed = chatSession.DismissOrphanedEscalations(sessionId);
            if (dismissed > 0)
                logger.LogInformation(
                    "[Escalation] Müşteri ayrıldı, {Count} eskalasyon otomatik kapatıldı. session={Session}",
                    dismissed, sessionId);
        }
    }

    private async Task SendInitialStateAsync(string sessionId, Func<string, object?, Task> write)
    {
        var state = chatSession.GetStateOrDefault(sessionId);

        if (state.Mode == ChatMode.Human)
        {
            await write(StreamEventTypes.HumanJoined, new
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
                await write(StreamEventTypes.HandoffPending, new
                {
                    escalationId = pending.Id,
                    reason = pending.Reason,
                    createdAt = pending.CreatedAt
                });
            }
        }
    }

    private async Task ProcessBridgeMessagesAsync(
        string sessionId, Func<string, object?, Task> write, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in chatSession.SubscribeToUserAsync(sessionId, ct))
            {
                if (msg.Sender == ChatBridgeSender.BotTyping)
                {
                    await write(StreamEventTypes.BotTyping, new
                    {
                        sessionId,
                        on = string.Equals(msg.Text, "on", StringComparison.OrdinalIgnoreCase)
                    });
                    continue;
                }

                await write(StreamEventTypes.HumanMessage, new
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
