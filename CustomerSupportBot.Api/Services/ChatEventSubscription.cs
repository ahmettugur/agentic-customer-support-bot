// Services/ChatEventSubscription.cs
// Persistent SSE — chat session için mode change ve escalation lifecycle event'lerini
// ChatEventOrchestrator tarafından kullanılan SSE forwarder'a iletir.

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Chat session başına kalıcı SSE bağlantısı için event abonelikleri.
/// Mode değişiklikleri (Bot↔Human) ve escalation lifecycle olaylarını
/// <see cref="SseForwarder"/>'a yazar.
/// </summary>
public sealed class ChatEventSubscription : IDisposable
{
    private readonly string _sessionId;
    private readonly IChatModeRegistry _modeRegistry;
    private readonly IEscalationSink _escalationSink;
    private readonly SseForwarder _sse;

    private EventHandler<ChatSessionState>? _modeHandler;
    private EventHandler<EscalationRequest>? _escCreatedHandler;
    private EventHandler<EscalationRequest>? _escDecidedHandler;

    public ChatEventSubscription(
        string sessionId,
        IChatModeRegistry modeRegistry,
        IEscalationSink escalationSink,
        SseForwarder sse)
    {
        _sessionId = sessionId;
        _modeRegistry = modeRegistry;
        _escalationSink = escalationSink;
        _sse = sse;
    }

    /// <summary>Mode değişimi ve escalation handler'larını kaydeder.</summary>
    public void Subscribe()
    {
        SubscribeToModeChanges();
        SubscribeToEscalations();
    }

    private void SubscribeToModeChanges()
    {
        _modeHandler = (_, s) =>
        {
            if (s.SessionId != _sessionId) return;

            if (s.Mode == ChatMode.Human)
            {
                _ = _sse.WriteAsync(StreamEventTypes.HumanJoined, new
                {
                    sessionId = _sessionId,
                    humanAgent = s.HumanAgent ?? WellKnown.Defaults.Admin,
                    enteredAt = s.EnteredAt
                });
            }
            else
            {
                _ = _sse.WriteAsync(StreamEventTypes.HumanLeft, new { sessionId = _sessionId });
            }
        };

        _modeRegistry.ModeChanged += _modeHandler;
    }

    private void SubscribeToEscalations()
    {
        // Yeni eskalasyon → kullanıcıda "bekleyin" banner'ı
        _escCreatedHandler = (_, esc) =>
        {
            if (esc.SessionId != _sessionId) return;

            _ = _sse.WriteAsync(StreamEventTypes.HandoffPending, new
            {
                escalationId = esc.Id,
                reason = esc.Reason,
                createdAt = esc.CreatedAt
            });
        };

        // Eskalasyon resolve/dismiss → banner'ı temizle (sadece bot modaysa)
        _escDecidedHandler = (_, esc) =>
        {
            if (esc.SessionId != _sessionId) return;

            if (esc.Status != EscalationStatus.Resolved && esc.Status != EscalationStatus.Dismissed)
                return;

            if (_modeRegistry.GetMode(_sessionId) == ChatMode.Bot)
            {
                _ = _sse.WriteAsync(StreamEventTypes.HandoffCleared, new
                {
                    escalationId = esc.Id,
                    status = esc.Status.ToString().ToLowerInvariant()
                });
            }
        };

        _escalationSink.RequestCreated += _escCreatedHandler;
        _escalationSink.RequestDecided += _escDecidedHandler;
    }

    public void Dispose()
    {
        if (_modeHandler != null)
            _modeRegistry.ModeChanged -= _modeHandler;

        if (_escCreatedHandler != null)
            _escalationSink.RequestCreated -= _escCreatedHandler;

        if (_escDecidedHandler != null)
            _escalationSink.RequestDecided -= _escDecidedHandler;
    }
}

