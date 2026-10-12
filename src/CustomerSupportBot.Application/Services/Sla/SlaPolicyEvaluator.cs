using CustomerSupportBot.Application.Ports.Outbound.Persistence;
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
        ISlaEventSink sink,
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
        ISlaEventSink sink,
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

    public const string KindComplaintPickup = "complaint_pickup";
    public const string KindComplaintResolve = "complaint_resolve";

    /// <summary>
    /// Açık şikayet için uyarı/ihlal olayları. Üstlenme eşikleri yalnız Beklemede'de; açık kalma eşikleri her
    /// açık durumda. Her (tür, şikayet, önem) için bir kez (<see cref="ISlaEventSink.LastEmittedAt"/>). Yeniden
    /// açılan şikayet için aynı tür olay tekrar yazılmaz (spec'te kabul edilen sınır).
    /// </summary>
    public static IReadOnlyList<SlaEvent> EvaluateComplaint(string complaintId, ComplaintInfo c, ComplaintSlaOptions o,
        ISlaEventSink sink, DateTime now)
    {
        var events = new List<SlaEvent>();
        if (!WellKnown.ComplaintStatuses.IsOpen(c.Status) || c.CreatedAt == default) return events;
        var age = (int)Math.Floor((now - c.CreatedAt).TotalSeconds);

        if (c.Status == WellKnown.ComplaintStatuses.Pending)
            Add(KindComplaintPickup, o.PickupWarnAfterSeconds, o.PickupBreachAfterSeconds, "Üstlenilmedi");
        Add(KindComplaintResolve, o.ResolveWarnAfterSeconds, o.ResolveBreachAfterSeconds, "Açık");
        return events;

        void Add(string kind, int warn, int breach, string what)
        {
            var severity = age >= breach ? SeverityBreach : age >= warn ? SeverityWarn : null;
            if (severity is null || sink.LastEmittedAt(kind, complaintId, severity) is not null) return;
            events.Add(new SlaEvent
            {
                Kind = kind, Severity = severity, TargetId = complaintId, AgeSeconds = age,
                Note = $"{what} {age}s — eşik {(severity == SeverityBreach ? breach : warn)}s"
            });
        }
    }

    /// <summary>Panel rengi: "none" (kapalı), "breach", "warn", "ok". Beklemede ise üstlenme, değilse çözüm eşikleri.</summary>
    public static string ComplaintTone(ComplaintInfo c, ComplaintSlaOptions o, DateTime nowUtc)
    {
        if (!WellKnown.ComplaintStatuses.IsOpen(c.Status) || c.CreatedAt == default) return "none";
        var age = (nowUtc - c.CreatedAt).TotalSeconds;
        var pending = c.Status == WellKnown.ComplaintStatuses.Pending;
        if (age >= o.ResolveBreachAfterSeconds || (pending && age >= o.PickupBreachAfterSeconds)) return "breach";
        if (age >= o.ResolveWarnAfterSeconds || (pending && age >= o.PickupWarnAfterSeconds)) return "warn";
        return "ok";
    }
}
