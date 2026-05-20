// Application/Services/HitlEventPortService.cs
// DRIVING PORT IMPL — IHitlEventPort → approval/escalation event aboneliği.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

public sealed class HitlEventPortService : IHitlEventPort
{
    private readonly IApprovalQueue _approvals;
    private readonly IEscalationSink _escalations;
    private readonly IChatModeRegistry _modeRegistry;

    public HitlEventPortService(IApprovalQueue approvals, IEscalationSink escalations, IChatModeRegistry modeRegistry)
    {
        _approvals = approvals;
        _escalations = escalations;
        _modeRegistry = modeRegistry;
    }

    public IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent)
    {
        return new ApprovalEscalationSubscription(_approvals, _escalations, sessionId, onEvent);
    }

    public IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent)
    {
        return new ChatEventSubscription(_modeRegistry, _escalations, sessionId, onEvent);
    }

    private sealed class ApprovalEscalationSubscription : IHitlEventSubscription
    {
        private readonly IApprovalQueue _approvals;
        private readonly IEscalationSink _escalations;
        private readonly string _sessionId;
        private readonly Func<string, object, Task> _onEvent;
        private readonly EventHandler<ApprovalRequest> _approvalCreatedHandler;
        private readonly EventHandler<ApprovalRequest> _approvalDecidedHandler;
        private readonly EventHandler<EscalationRequest> _escalationCreatedHandler;

        public ApprovalEscalationSubscription(
            IApprovalQueue approvals,
            IEscalationSink escalations,
            string sessionId,
            Func<string, object, Task> onEvent)
        {
            _approvals = approvals;
            _escalations = escalations;
            _sessionId = sessionId;
            _onEvent = onEvent;

            _approvalCreatedHandler = (_, req) =>
            {
                if (req.SessionId == _sessionId)
                {
                    FireAndForget(_onEvent(StreamEventTypes.ApprovalRequired, req));
                }
            };

            _approvalDecidedHandler = (_, req) =>
            {
                if (req.SessionId == _sessionId)
                {
                    FireAndForget(_onEvent(StreamEventTypes.ApprovalResolved, new
                    {
                        id = req.Id,
                        status = req.Status.ToString().ToLowerInvariant(),
                        reason = req.DecisionReason,
                        decidedBy = req.DecidedBy
                    }));
                }
            };

            _escalationCreatedHandler = (_, req) =>
            {
                if (req.SessionId == _sessionId)
                {
                    FireAndForget(_onEvent(StreamEventTypes.EscalationCreated, req));
                }
            };

            _approvals.RequestCreated += _approvalCreatedHandler;
            _approvals.RequestDecided += _approvalDecidedHandler;
            _escalations.RequestCreated += _escalationCreatedHandler;
        }

        public void Dispose()
        {
            _approvals.RequestCreated -= _approvalCreatedHandler;
            _approvals.RequestDecided -= _approvalDecidedHandler;
            _escalations.RequestCreated -= _escalationCreatedHandler;
        }

        private static void FireAndForget(Task task)
        {
            _ = task.ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private sealed class ChatEventSubscription : IHitlEventSubscription
    {
        private readonly IChatModeRegistry _modeRegistry;
        private readonly IEscalationSink _escalations;
        private readonly string _sessionId;
        private readonly Func<string, object, Task> _onEvent;
        private readonly EventHandler<ChatSessionState> _modeHandler;
        private readonly EventHandler<EscalationRequest> _escCreatedHandler;
        private readonly EventHandler<EscalationRequest> _escDecidedHandler;

        public ChatEventSubscription(
            IChatModeRegistry modeRegistry,
            IEscalationSink escalations,
            string sessionId,
            Func<string, object, Task> onEvent)
        {
            _modeRegistry = modeRegistry;
            _escalations = escalations;
            _sessionId = sessionId;
            _onEvent = onEvent;

            _modeHandler = (_, s) =>
            {
                if (s.SessionId != _sessionId) return;
                if (s.Mode == ChatMode.Human)
                {
                    FireAndForget(_onEvent(StreamEventTypes.HumanJoined, new
                    {
                        sessionId = _sessionId,
                        humanAgent = s.HumanAgent ?? WellKnown.Defaults.Admin,
                        enteredAt = s.EnteredAt
                    }));
                }
                else
                {
                    FireAndForget(_onEvent(StreamEventTypes.HumanLeft, new { sessionId = _sessionId }));
                }
            };

            _escCreatedHandler = (_, esc) =>
            {
                if (esc.SessionId != _sessionId) return;
                FireAndForget(_onEvent(StreamEventTypes.HandoffPending, new
                {
                    escalationId = esc.Id,
                    reason = esc.Reason,
                    createdAt = esc.CreatedAt
                }));
            };

            _escDecidedHandler = (_, esc) =>
            {
                if (esc.SessionId != _sessionId) return;
                if (esc.Status != EscalationStatus.Resolved && esc.Status != EscalationStatus.Dismissed) return;
                if (_modeRegistry.GetMode(_sessionId) == ChatMode.Bot)
                {
                    FireAndForget(_onEvent(StreamEventTypes.HandoffCleared, new
                    {
                        escalationId = esc.Id,
                        status = esc.Status.ToString().ToLowerInvariant()
                    }));
                }
            };

            _modeRegistry.ModeChanged += _modeHandler;
            _escalations.RequestCreated += _escCreatedHandler;
            _escalations.RequestDecided += _escDecidedHandler;
        }

        public void Dispose()
        {
            _modeRegistry.ModeChanged -= _modeHandler;
            _escalations.RequestCreated -= _escCreatedHandler;
            _escalations.RequestDecided -= _escDecidedHandler;
        }

        private static void FireAndForget(Task task)
        {
            _ = task.ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
