// Application/Services/SlaPortService.cs
// DRIVING PORT IMPL — ISlaPort → ISlaEventSink + IApprovalQueue + IEscalationSink.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Sla;

public sealed class SlaPortService : ISlaPort
{
    private readonly ISlaEventSink _events;
    private readonly IApprovalQueue _approvals;
    private readonly IEscalationSink _escalations;
    private readonly IOptionsMonitor<SlaOptions> _options;
    private readonly IComplaintRepository? _complaints;

    public SlaPortService(
        ISlaEventSink events,
        IApprovalQueue approvals,
        IEscalationSink escalations,
        IOptionsMonitor<SlaOptions> options,
        IComplaintRepository? complaints = null)
    {
        _events = events;
        _approvals = approvals;
        _escalations = escalations;
        _options = options;
        _complaints = complaints;
    }

    public IReadOnlyList<SlaEvent> GetRecentEvents(int count = 100)
        => _events.GetRecent(Math.Clamp(count, 1, 500));

    public async Task ScanOnceAsync(SlaOptions opts, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Kalıcı okuma: SLA'nın göremediği talep için ihlal de üretilmez.
        foreach (var req in await _approvals.GetPendingAsync(ct))
        {
            var eval = SlaPolicyEvaluator.EvaluateApproval(req, opts.Approvals, _events, now);
            if (eval.WarnEvent is not null) await _events.RecordAsync(eval.WarnEvent);
            if (eval.BreachEvent is not null)
            {
                await _events.RecordAsync(eval.BreachEvent);
                await ApplyApprovalBreachAsync(req, eval.BreachAction, ct);
            }
        }

        foreach (var esc in _escalations.GetOpen())
        {
            var eval = SlaPolicyEvaluator.EvaluateEscalation(esc, opts.Escalations, _events, now);
            if (eval.WarnEvent is not null) await _events.RecordAsync(eval.WarnEvent);
            if (eval.BreachEvent is not null)
            {
                await _events.RecordAsync(eval.BreachEvent);
                if (eval.NewPriority.HasValue && eval.NewPriority.Value != esc.Priority)
                    esc.Priority = eval.NewPriority.Value;
            }
        }

        if (_complaints is not null)
        {
            foreach (var item in await _complaints.GetOpenAsync(500, ct))
                foreach (var evt in SlaPolicyEvaluator.EvaluateComplaint(item.ComplaintId, item.Complaint, opts.Complaints, _events, now))
                    await _events.RecordAsync(evt);
        }
    }

    private async Task ApplyApprovalBreachAsync(ApprovalRequest req, SlaBreachAction action, CancellationToken ct)
    {
        switch (action)
        {
            case SlaBreachAction.AutoReject:
                await _approvals.DecideAsync(req.Id, approved: false,
                    decidedBy: WellKnown.Defaults.System,
                    reason: "SLA breach — auto-reject", ct: ct);
                break;
            case SlaBreachAction.AutoApprove:
                await _approvals.DecideAsync(req.Id, approved: true,
                    decidedBy: WellKnown.Defaults.System,
                    reason: "SLA breach — auto-approve", ct: ct);
                break;
        }
    }

    public async Task<SlaStatusResult> GetStatusAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var now = DateTime.UtcNow;

        var pending = await _approvals.GetPendingAsync(ct);
        var open = _escalations.GetOpen();
        var recent = _events.GetRecent(200);

        var pendingAges = pending.Select(p => (int)Math.Floor((now - p.RequestedAt).TotalSeconds)).ToList();
        var openAges = open.Select(e => (int)Math.Floor((now - e.CreatedAt).TotalSeconds)).ToList();

        SlaComplaintStatus? complaintStatus = null;
        if (_complaints is not null)
        {
            var openComplaints = await _complaints.GetOpenAsync(5000, ct);
            var ages = openComplaints.Where(i => i.Complaint.CreatedAt != default)
                .Select(i => (int)Math.Floor((now - i.Complaint.CreatedAt).TotalSeconds)).ToList();
            complaintStatus = new SlaComplaintStatus(
                openComplaints.Count,
                openComplaints.Count(i => i.Complaint.Status == WellKnown.ComplaintStatuses.Pending),
                ages.Count > 0 ? ages.Max() : 0,
                opts.Complaints.PickupWarnAfterSeconds, opts.Complaints.PickupBreachAfterSeconds,
                opts.Complaints.ResolveWarnAfterSeconds, opts.Complaints.ResolveBreachAfterSeconds,
                recent.Count(e => e.Kind is SlaPolicyEvaluator.KindComplaintPickup or SlaPolicyEvaluator.KindComplaintResolve
                               && e.Severity == SlaPolicyEvaluator.SeverityBreach));
        }

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
                               && e.Severity == SlaPolicyEvaluator.SeverityBreach)),
            complaintStatus);
    }
}
