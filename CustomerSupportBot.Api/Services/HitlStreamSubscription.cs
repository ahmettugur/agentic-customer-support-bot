// Services/HitlStreamSubscription.cs
// HITL (approval + escalation) event'lerini SSE forwarder'a yönlendiren subscription.
// ChatStreamOrchestrator tarafından kullanılır — workflow süresince aktif kalır.

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// HITL event'lerini (approval required/resolved, escalation created) belirli bir
/// session için filtreler ve <see cref="SseForwarder"/>'a yazar. RAII benzeri:
/// Subscribe → workflow boyunca aktif → Unsubscribe ile temizlenir.
/// </summary>
public sealed class HitlStreamSubscription : IDisposable
{
    private readonly IApprovalQueue _approvalQueue;
    private readonly IEscalationSink _escalationSink;
    private readonly SseForwarder _sse;
    private readonly string _sessionId;

    private EventHandler<ApprovalRequest>? _approvalCreatedHandler;
    private EventHandler<ApprovalRequest>? _approvalDecidedHandler;
    private EventHandler<EscalationRequest>? _escalationHandler;

    public HitlStreamSubscription(
        IApprovalQueue approvalQueue,
        IEscalationSink escalationSink,
        SseForwarder sse,
        string sessionId)
    {
        _approvalQueue = approvalQueue;
        _escalationSink = escalationSink;
        _sse = sse;
        _sessionId = sessionId;
    }

    /// <summary>Event handler'ları kaydeder.</summary>
    public void Subscribe()
    {
        _approvalCreatedHandler = (_, req) =>
        {
            if (req.SessionId != _sessionId) return;
            _ = _sse.WriteAsync(StreamEventTypes.ApprovalRequired, req);
        };

        _approvalDecidedHandler = (_, req) =>
        {
            if (req.SessionId != _sessionId) return;
            _ = _sse.WriteAsync(StreamEventTypes.ApprovalResolved, new
            {
                id = req.Id,
                status = req.Status.ToString().ToLowerInvariant(),
                reason = req.DecisionReason,
                decidedBy = req.DecidedBy
            });
        };

        _escalationHandler = (_, req) =>
        {
            if (req.SessionId != _sessionId) return;
            _ = _sse.WriteAsync(StreamEventTypes.EscalationCreated, req);
        };

        _approvalQueue.RequestCreated += _approvalCreatedHandler;
        _approvalQueue.RequestDecided += _approvalDecidedHandler;
        _escalationSink.RequestCreated += _escalationHandler;
    }

    /// <summary>Tüm event handler'ları kaldırır.</summary>
    public void Unsubscribe()
    {
        if (_approvalCreatedHandler != null)
            _approvalQueue.RequestCreated -= _approvalCreatedHandler;

        if (_approvalDecidedHandler != null)
            _approvalQueue.RequestDecided -= _approvalDecidedHandler;

        if (_escalationHandler != null)
            _escalationSink.RequestCreated -= _escalationHandler;
    }

    public void Dispose() => Unsubscribe();
}

