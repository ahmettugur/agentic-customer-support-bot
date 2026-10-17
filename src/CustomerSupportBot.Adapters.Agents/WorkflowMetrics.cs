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

    public static void Record(string status, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("status", status);
        Completions.Add(1, tag);
        Duration.Record(elapsed.TotalMilliseconds, tag);
    }
}
