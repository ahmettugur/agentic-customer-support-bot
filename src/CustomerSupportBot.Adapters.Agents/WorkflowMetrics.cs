// Adapters.Agents/WorkflowMetrics.cs
// Workflow koşusu metrikleri. Telemetry adaptörüne bağımlı olmamak için BCL Meter'ı, uygulamanın ortak
// Meter adıyla (TelemetryConstants.MeterName) açar — OpenTelemetry bu adı AddMeter ile zaten dinler.

using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Adapters.Agents;

internal static class WorkflowMetrics
{
    private static readonly Meter Meter = new(TelemetryConstants.MeterName, "1.0.0");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "agent.workflow.duration", unit: "ms",
        description: "Bir kullanıcı isteği için workflow koşusunun süresi.");

    private static readonly Counter<long> Completions = Meter.CreateCounter<long>(
        "agent.workflow.completions", unit: "{run}",
        description: "Biten workflow koşuları (status: ok / timeout / cancelled / error).");

    private static readonly Counter<long> Recoveries = Meter.CreateCounter<long>(
        "agent.workflow.recoveries", unit: "{turn}",
        description: "Çökme sonrası devam taramasının sonuçları (result: resumed / discarded / failed).");

    private static readonly Counter<long> DecompositionFallbacks = Meter.CreateCounter<long>(
        "agent.decomposition.fallbacks", unit: "{turn}",
        description: "Bileşik plan reddedildi, sorgu bölünmeden tek koşuyla yürüdü (reason: structure / ungrounded_entity / cross_wired_entity).");

    public static void RecordDecompositionFallback(string reason) =>
        DecompositionFallbacks.Add(1, new KeyValuePair<string, object?>("reason", reason));

    private static readonly Counter<long> RoutingDecisions = Meter.CreateCounter<long>(
        "agent.routing.decisions", unit: "{turn}",
        description: "İlk ajan yönlendirmesi. mode=shadow: outcome=match|mismatch|router_abstained|unobserved (yönlendirici ↔ Planning); " +
                     "mode=deterministic: outcome=routed|fallback_to_planner.");

    public static void RecordRouting(string mode, string outcome, string router, string planner, string reason) =>
        RoutingDecisions.Add(1,
            new KeyValuePair<string, object?>("mode", mode), new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("router", router), new KeyValuePair<string, object?>("planner", planner),
            new KeyValuePair<string, object?>("reason", reason));

    public static void RecordRecovery(string result) =>
        Recoveries.Add(1, new KeyValuePair<string, object?>("result", result));

    public static void Record(string status, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("status", status);
        Completions.Add(1, tag);
        Duration.Record(elapsed.TotalMilliseconds, tag);
    }
}
