// Application/Services/SlaPortService.cs
// DRIVING PORT IMPL — ISlaPort → ISlaEventSink + IApprovalQueue + IEscalationSink.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services;

public sealed class SlaPortService : ISlaPort
{
    private readonly ISlaEventSink _events;
    private readonly IApprovalQueue _approvals;
    private readonly IEscalationSink _escalations;
    private readonly IOptionsMonitor<SlaOptions> _options;

    public SlaPortService(
        ISlaEventSink events,
        IApprovalQueue approvals,
        IEscalationSink escalations,
        IOptionsMonitor<SlaOptions> options)
    {
        _events = events;
        _approvals = approvals;
        _escalations = escalations;
        _options = options;
    }

    public IReadOnlyList<SlaEvent> GetRecentEvents(int count = 100)
        => _events.GetRecent(Math.Clamp(count, 1, 500));

    public SlaStatusResult GetStatus()
    {
        var opts = _options.CurrentValue;
        var now = DateTime.UtcNow;

        var pending = _approvals.GetPending();
        var open = _escalations.GetOpen();
        var recent = _events.GetRecent(200);

        var pendingAges = pending.Select(p => (int)Math.Floor((now - p.RequestedAt).TotalSeconds)).ToList();
        var openAges = open.Select(e => (int)Math.Floor((now - e.CreatedAt).TotalSeconds)).ToList();

        return new SlaStatusResult(
            opts.Enabled,
            opts.PollIntervalSeconds,
            new SlaApprovalStatus(
                pending.Count,
                pendingAges.Count > 0 ? pendingAges.Max() : 0,
                opts.Approvals.WarnAfterSeconds,
                opts.Approvals.BreachAfterSeconds,
                opts.Approvals.OnBreach.ToString(),
                recent.Count(e => e.Kind == SlaPolicyEvaluator.KindApproval
                               && e.Severity == SlaPolicyEvaluator.SeverityBreach)),
            new SlaEscalationStatus(
                open.Count,
                openAges.Count > 0 ? openAges.Max() : 0,
                opts.Escalations.WarnAfterSeconds,
                opts.Escalations.BreachAfterSeconds,
                opts.Escalations.BoostPriorityOnBreach,
                recent.Count(e => e.Kind == SlaPolicyEvaluator.KindEscalation
                               && e.Severity == SlaPolicyEvaluator.SeverityBreach)));
    }
}
