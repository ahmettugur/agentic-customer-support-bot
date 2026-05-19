// Adapters.Telemetry/DependencyInjection/TelemetryAdapterServiceCollectionExtensions.cs
// Telemetri adapter'ları için DI kayıtları.

using CustomerSupportBot.Adapters.Telemetry.Models;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Driven.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Resources;
using global::OpenTelemetry.Trace;

namespace CustomerSupportBot.Adapters.Telemetry.DependencyInjection;

/// <summary>
/// Telemetri driven adapter'larını DI container'a kaydeder.
/// OpenTelemetry pipeline'ını yapılandırır.
/// </summary>
public static class TelemetryAdapterServiceCollectionExtensions
{
    /// <summary>
    /// ICostCalculatorPort adaptörünü ve OpenTelemetry pipeline'ını kaydeder.
    /// </summary>
    public static IServiceCollection AddTelemetryAdapters(
        this IServiceCollection services,
        IConfiguration configuration,
        string activitySourceName = "CustomerSupportBot",
        string meterName = "CustomerSupportBot")
    {
        services.Configure<TelemetryAdapterOptions>(configuration.GetSection(TelemetryAdapterOptions.SectionName));

        var options = configuration.GetSection(TelemetryAdapterOptions.SectionName)
            .Get<TelemetryAdapterOptions>() ?? new TelemetryAdapterOptions();

        // ICostCalculatorPort adapter
        services.AddSingleton<ICostCalculatorPort, OpenTelemetry.CostCalculator>();

        if (!options.Enabled)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(options.ServiceName, serviceVersion: options.ServiceVersion))
            .WithTracing(tracing =>
            {
                if (!options.TracingEnabled) return;

                tracing
                    .AddSource(activitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.Otlp.Endpoint))
                {
                    tracing.AddOtlpExporter(o => ConfigureOtlp(o, options.Otlp));
                }
            })
            .WithMetrics(metrics =>
            {
                if (!options.MetricsEnabled) return;

                metrics
                    .AddMeter(meterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.Otlp.Endpoint))
                {
                    metrics.AddOtlpExporter(o => ConfigureOtlp(o, options.Otlp));
                }
            });

        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions otlpOptions, TelemetryAdapterOptions.OtlpExporterOptions src)
    {
        otlpOptions.Endpoint = new Uri(src.Endpoint!);
        otlpOptions.Protocol = src.Protocol?.ToLowerInvariant() switch
        {
            "httpprotobuf" or "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc
        };
        if (!string.IsNullOrWhiteSpace(src.Headers))
        {
            otlpOptions.Headers = src.Headers;
        }
    }
}
