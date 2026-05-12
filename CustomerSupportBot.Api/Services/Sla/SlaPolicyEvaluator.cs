// Services/Sla/SlaPolicyEvaluator.cs
// SLA Guardian'ın saf (yan etkisiz) karar verici motoru. Kuyrukları girdi
// olarak alır, üretilmesi gereken SlaEvent'leri ve uygulanacak aksiyonları
// döndürür. BackgroundService ve unit testler ortak olarak bunu kullanır.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services.Sla;

public static class SlaPolicyEvaluator
{
    public const string KindApproval = "approval";
    public const string KindEscalation = "escalation";
    public const string SeverityWarn = "warn";
    public const string SeverityBreach = "breach";

    /// <summary>SLA değerlendirmesinin tek bir kuyruk için sonucu.</summary>
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

    /// <summary>
    /// Pending bir onay isteğini SLA politikasına karşı değerlendirir.
    /// Daha önce yayınlanan event'lerin tekrar üretilmemesi için sink ile karşılaştırma yapılır.
    /// </summary>
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

    /// <summary>
    /// Açık eskalasyonu SLA politikasına karşı değerlendirir. Breach olunca
    /// öncelik bir kademe yükseltilir (Critical sabit).
    /// </summary>
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
                {
                    newPriority = BoostPriority(request.Priority);
                }
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

    /// <summary>Önceliği bir kademe yükseltir; Critical en üstte sabit kalır.</summary>
    public static EscalationPriority BoostPriority(EscalationPriority current) => current switch
    {
        EscalationPriority.Low => EscalationPriority.Normal,
        EscalationPriority.Normal => EscalationPriority.High,
        EscalationPriority.High => EscalationPriority.Critical,
        EscalationPriority.Critical => EscalationPriority.Critical,
        _ => EscalationPriority.Normal
    };
}
