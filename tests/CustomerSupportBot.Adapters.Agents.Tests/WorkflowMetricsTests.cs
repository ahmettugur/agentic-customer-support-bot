// Tests/WorkflowMetricsTests.cs
//
// Workflow koşusu metrikleri: Prometheus alarmı (WorkflowFailureRateHigh) "status" etiketine dayanır.
// Etiket değerleri ya da sayaç adı sessizce değişirse alarm hiç tetiklenmez — burada kilitlenir.

using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class WorkflowMetricsTests
{
    [Fact]
    public void Record_WritesCompletionCounterAndDuration_WithStatusTag()
    {
        var completions = new List<(long Value, string? Status)>();
        var durations = new List<(double Value, string? Status)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            if (inst.Name == "agent.workflow.completions") completions.Add((value, StatusOf(tags)));
        });
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            if (inst.Name == "agent.workflow.duration") durations.Add((value, StatusOf(tags)));
        });
        listener.Start();

        WorkflowMetrics.Record("timeout", TimeSpan.FromSeconds(2));

        Assert.Contains((1L, "timeout"), completions);
        Assert.Contains((2000d, "timeout"), durations);
    }

    private static string? StatusOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
            if (tag.Key == "status") return tag.Value as string;
        return null;
    }
}
