// Endpoints/SlaEndpoints.cs
// SLA Guardian admin endpoint'leri:
//   GET /sla/events    — son N warn/breach olayı
//   GET /sla/status    — güncel pending/open kuyruk + max yaş + ihlal sayısı

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services.Sla;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class SlaEndpoints
{
    public static IEndpointRouteBuilder MapSlaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/events", (
            ISlaEventSink sink,
            int? count) =>
        {
            var n = Math.Clamp(count ?? 100, 1, 500);
            var items = sink.GetRecent(n);
            return Results.Ok(new { count = items.Count, items });
        });

        app.MapGet("/sla/status", (
            IApprovalQueue approvals,
            IEscalationSink escalations,
            ISlaEventSink sink,
            IOptionsMonitor<SlaOptions> options) =>
        {
            var opts = options.CurrentValue;
            var now = DateTime.UtcNow;
            var pending = approvals.GetPending();
            var open = escalations.GetOpen();

            var pendingAges = pending
                .Select(p => (int)Math.Floor((now - p.RequestedAt).TotalSeconds))
                .ToList();
            var openAges = open
                .Select(e => (int)Math.Floor((now - e.CreatedAt).TotalSeconds))
                .ToList();

            var recent = sink.GetRecent(200);

            return Results.Ok(new
            {
                enabled = opts.Enabled,
                pollIntervalSeconds = opts.PollIntervalSeconds,
                approvals = new
                {
                    pendingCount = pending.Count,
                    oldestSeconds = pendingAges.Count > 0 ? pendingAges.Max() : 0,
                    warnAfter = opts.Approvals.WarnAfterSeconds,
                    breachAfter = opts.Approvals.BreachAfterSeconds,
                    onBreach = opts.Approvals.OnBreach.ToString(),
                    breachCountRecent = recent.Count(e =>
                        e.Kind == SlaPolicyEvaluator.KindApproval &&
                        e.Severity == SlaPolicyEvaluator.SeverityBreach)
                },
                escalations = new
                {
                    openCount = open.Count,
                    oldestSeconds = openAges.Count > 0 ? openAges.Max() : 0,
                    warnAfter = opts.Escalations.WarnAfterSeconds,
                    breachAfter = opts.Escalations.BreachAfterSeconds,
                    boostPriorityOnBreach = opts.Escalations.BoostPriorityOnBreach,
                    breachCountRecent = recent.Count(e =>
                        e.Kind == SlaPolicyEvaluator.KindEscalation &&
                        e.Severity == SlaPolicyEvaluator.SeverityBreach)
                }
            });
        });

        return app;
    }
}

