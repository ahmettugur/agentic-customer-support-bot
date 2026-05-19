using CustomerSupportBot.Domain.Model;
// Extensions/TelemetryExtensions.cs
// OpenTelemetry tracing + metric pipeline'ı kurar. Tüm domain ActivitySource ve
// Meter'ları (CustomerSupportTelemetry), ASP.NET Core, HttpClient ve EF Core
// instrumentation'ı dahil eder. OTLP endpoint configure edildiyse exporter'ı bağlar;
// development'ta console exporter opsiyonel olarak açılabilir.
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Driven.Observability;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CustomerSupportBot.Api.Extensions;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetryServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
                      ?? new TelemetryOptions();

        // Domain singleton'ları — telemetri kapalıyken de çalışmalı (no-op olur)
        services.AddSingleton<ICostCalculatorPort, Adapters.Telemetry.OpenTelemetry.CostCalculator>();
        services.AddSingleton<CostUsageStore>();

        if (!options.Enabled)
        {
            return services;
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: options.ServiceName, serviceVersion: options.ServiceVersion);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(options.ServiceName, serviceVersion: options.ServiceVersion))
            .WithTracing(tracing =>
            {
                if (!options.TracingEnabled) return;

                tracing
                    .AddSource(CustomerSupportTelemetry.ActivitySourceName)
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
                    .AddMeter(CustomerSupportTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.Otlp.Endpoint))
                {
                    metrics.AddOtlpExporter(o => ConfigureOtlp(o, options.Otlp));
                }
            });

        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions otlpOptions, TelemetryOptions.OtlpExporterOptions src)
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

