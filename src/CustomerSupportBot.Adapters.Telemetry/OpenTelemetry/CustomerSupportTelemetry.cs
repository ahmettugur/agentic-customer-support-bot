// Adapters.Telemetry/OpenTelemetry/CustomerSupportTelemetry.cs
// Tüm uygulama için tek noktadan ActivitySource + Meter sağlar.
// Her ajan / tool / LLM çağrısı bu kaynak üzerinden span üretir; metric counter'lar
// burada tanımlanan Meter üzerinden yazılır. OpenTelemetry pipeline'ı bu Source/Meter
// adlarını dinleyerek topladıkları veriyi exporter'lara aktarır.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;

/// <summary>
/// Uygulama genelinde paylaşılan telemetri primitifleri.
/// İsimler OpenTelemetry semantic convention'larına yakın tutulmuştur.
/// </summary>
public static class CustomerSupportTelemetry
{
    public const string ActivitySourceName = TelemetryConstants.ActivitySourceName;
    public const string MeterName = TelemetryConstants.MeterName;

    /// <summary>Tüm domain span'lerinin ortak kaynağı.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    /// <summary>Tüm domain metric'lerinin ortak meter'ı.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // ─── Counter / Histogram'lar ───

    /// <summary>LLM çağrısı sayısı (model + provider tag'leri ile).</summary>
    public static readonly Counter<long> LlmCallsCounter =
        Meter.CreateCounter<long>("ai.llm.calls", unit: "{call}",
            description: "Toplam LLM (chat/reasoning) çağrı sayısı.");

    /// <summary>Toplam input token sayısı.</summary>
    public static readonly Counter<long> InputTokensCounter =
        Meter.CreateCounter<long>("ai.tokens.input", unit: "{token}",
            description: "Toplam input (prompt) token sayısı.");

    /// <summary>Toplam output token sayısı.</summary>
    public static readonly Counter<long> OutputTokensCounter =
        Meter.CreateCounter<long>("ai.tokens.output", unit: "{token}",
            description: "Toplam output (completion) token sayısı.");

    /// <summary>USD bazında toplam tahmini maliyet.</summary>
    public static readonly Counter<double> CostUsdCounter =
        Meter.CreateCounter<double>("ai.cost.usd", unit: "USD",
            description: "Tahmini toplam LLM maliyeti (USD).");

    /// <summary>LLM çağrı süresi (ms).</summary>
    public static readonly Histogram<double> LlmLatencyHistogram =
        Meter.CreateHistogram<double>("ai.llm.duration", unit: "ms",
            description: "LLM çağrı tamamlanma süresi.");

    // agent.workflow.duration / agent.workflow.completions: Adapters.Agents/WorkflowMetrics.cs (aynı Meter adı).

    // ─── Activity (span) yardımcıları ───

    /// <summary>Bir agent activity'si başlatır (Internal).</summary>
    public static Activity? StartAgentActivity(string agentName, string? sessionId = null, string? traceId = null)
    {
        var activity = ActivitySource.StartActivity($"agent.{agentName}", ActivityKind.Internal);
        if (activity == null) return null;
        activity.SetTag("agent.name", agentName);
        if (!string.IsNullOrEmpty(sessionId)) activity.SetTag("session.id", sessionId);
        if (!string.IsNullOrEmpty(traceId)) activity.SetTag("trace.id", traceId);
        return activity;
    }

    /// <summary>Bir tool çağrısı için activity başlatır (Internal).</summary>
    public static Activity? StartToolActivity(string toolName, string? sessionId = null)
    {
        var activity = ActivitySource.StartActivity($"tool.{toolName}", ActivityKind.Internal);
        if (activity == null) return null;
        activity.SetTag("tool.name", toolName);
        if (!string.IsNullOrEmpty(sessionId)) activity.SetTag("session.id", sessionId);
        return activity;
    }

    /// <summary>Bir LLM çağrısı için activity başlatır (Client).</summary>
    public static Activity? StartLlmActivity(string operation, string? model = null, string? provider = null)
    {
        var activity = ActivitySource.StartActivity($"ai.{operation}", ActivityKind.Client);
        if (activity == null) return null;
        if (!string.IsNullOrEmpty(model)) activity.SetTag("ai.model", model);
        if (!string.IsNullOrEmpty(provider)) activity.SetTag("ai.provider", provider);
        return activity;
    }
}
