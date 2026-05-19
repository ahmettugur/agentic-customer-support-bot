using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Sla;

public static class SlaPolicyEvaluator
{
    public const string KindApproval = "approval";
    public const string KindEscalation = "escalation";
    public const string SeverityWarn = "warn";
    public const string SeverityBreach = "breach";

    public record ApprovalEvaluation(
        ApprovalRequest Request,
        SlaEvent? WarnEvent,
        SlaEvent? BreachEvent,
        SlaBreachAction BreachAction);

    public record EscalationEvaluation(
        EscalationRequest Request,
        SlaEvent? WarnEvent,
        SlaEvent? BreachEvent,
        EscalationPriority? NewPriority);

    public static ApprovalEvaluation EvaluateApproval(
        ApprovalRequest request,
        ApprovalSlaOptions options,
        ISlaEventRepository sink,
        DateTime now)
    {
        var age = (int)Math.Floor((now - request.RequestedAt).TotalSeconds);

        SlaEvent? warn = null;
        SlaEvent? breach = null;
        var action = SlaBreachAction.None;

        if (age >= options.BreachAfterSeconds)
        {
            if (sink.LastEmittedAt(KindApproval, request.Id, SeverityBreach) is null)
            {
                action = options.OnBreach;
                breach = new SlaEvent
                {
                    Kind = KindApproval,
                    Severity = SeverityBreach,
                    TargetId = request.Id,
                    AgeSeconds = age,
                    Action = action.ToString(),
                    Note = $"Pending {age}s — threshold {options.BreachAfterSeconds}s aşıldı"
                };
            }
        }
        else if (age >= options.WarnAfterSeconds)
        {
            if (sink.LastEmittedAt(KindApproval, request.Id, SeverityWarn) is null)
            {
                warn = new SlaEvent
                {
                    Kind = KindApproval,
                    Severity = SeverityWarn,
                    TargetId = request.Id,
                    AgeSeconds = age,
                    Note = $"Pending {age}s — uyarı eşiği {options.WarnAfterSeconds}s"
                };
            }
        }

        return new ApprovalEvaluation(request, warn, breach, action);
    }

    public static EscalationEvaluation EvaluateEscalation(
        EscalationRequest request,
        EscalationSlaOptions options,
        ISlaEventRepository sink,
        DateTime now)
    {
        var age = (int)Math.Floor((now - request.CreatedAt).TotalSeconds);

        SlaEvent? warn = null;
        SlaEvent? breach = null;
        EscalationPriority? newPriority = null;

        if (age >= options.BreachAfterSeconds)
        {
            if (sink.LastEmittedAt(KindEscalation, request.Id, SeverityBreach) is null)
            {
                if (options.BoostPriorityOnBreach)
                    newPriority = BoostPriority(request.Priority);

                breach = new SlaEvent
                {
                    Kind = KindEscalation,
                    Severity = SeverityBreach,
                    TargetId = request.Id,
                    AgeSeconds = age,
                    Action = newPriority.HasValue && newPriority != request.Priority
                        ? $"PriorityBoost:{request.Priority}->{newPriority}"
                        : "None",
                    Note = $"Açık {age}s — threshold {options.BreachAfterSeconds}s aşıldı"
                };
            }
        }
        else if (age >= options.WarnAfterSeconds)
        {
            if (sink.LastEmittedAt(KindEscalation, request.Id, SeverityWarn) is null)
            {
                warn = new SlaEvent
                {
                    Kind = KindEscalation,
                    Severity = SeverityWarn,
                    TargetId = request.Id,
                    AgeSeconds = age,
                    Note = $"Açık {age}s — uyarı eşiği {options.WarnAfterSeconds}s"
                };
            }
        }

        return new EscalationEvaluation(request, warn, breach, newPriority);
    }

    public static EscalationPriority BoostPriority(EscalationPriority current) => current switch
    {
        EscalationPriority.Low => EscalationPriority.Normal,
        EscalationPriority.Normal => EscalationPriority.High,
        EscalationPriority.High => EscalationPriority.Critical,
        EscalationPriority.Critical => EscalationPriority.Critical,
        _ => EscalationPriority.Normal
    };
}
