// Application/Services/HitlEventPortService.cs
// DRIVING PORT IMPL — IHitlEventPort → approval/escalation event aboneliği.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Escalation;

public sealed class HitlEventPortService : IHitlEventPort
{
    private readonly IApprovalQueue _approvals;
    private readonly IEscalationSink _escalations;
    private readonly IChatModeRegistry _modeRegistry;
    private readonly ILogger<HitlEventPortService> _logger;

    public HitlEventPortService(
        IApprovalQueue approvals,
        IEscalationSink escalations,
        IChatModeRegistry modeRegistry,
        ILogger<HitlEventPortService>? logger = null)
    {
        _approvals = approvals;
        _escalations = escalations;
        _modeRegistry = modeRegistry;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HitlEventPortService>.Instance;
    }

    public IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent)
    {
        return new ApprovalEscalationSubscription(_approvals, _escalations, sessionId, onEvent, _logger);
    }

    public IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent)
    {
        return new ChatEventSubscription(_modeRegistry, _escalations, sessionId, onEvent, _logger);
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
        private readonly ILogger _logger;

        public ApprovalEscalationSubscription(
            IApprovalQueue approvals,
            IEscalationSink escalations,
            string sessionId,
            Func<string, object, Task> onEvent,
            ILogger logger)
        {
            _approvals = approvals;
            _escalations = escalations;
            _sessionId = sessionId;
            _onEvent = onEvent;
            _logger = logger;

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

        private void FireAndForget(Task task)
        {
            _ = task.ContinueWith(t =>
            {
                _logger.LogWarning(t.Exception?.GetBaseException(),
                    "[HITL] Event handler failed (session={SessionId})", _sessionId);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
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
        private readonly ILogger _logger;

        public ChatEventSubscription(
            IChatModeRegistry modeRegistry,
            IEscalationSink escalations,
            string sessionId,
            Func<string, object, Task> onEvent,
            ILogger logger)
        {
            _modeRegistry = modeRegistry;
            _escalations = escalations;
            _sessionId = sessionId;
            _onEvent = onEvent;
            _logger = logger;

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

        private void FireAndForget(Task task)
        {
            _ = task.ContinueWith(t =>
            {
                _logger.LogWarning(t.Exception?.GetBaseException(),
                    "[HITL] Chat event handler failed (session={SessionId})", _sessionId);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}
