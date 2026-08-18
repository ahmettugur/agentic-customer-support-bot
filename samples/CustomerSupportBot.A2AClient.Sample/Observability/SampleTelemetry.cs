// Bu istemcinin OpenTelemetry kaynağı ve isteğe bağlı ihraç boru hattı.
//
// Sunucudaki desenin (Adapters.Telemetry/DependencyInjection/TelemetryAdapterServiceCollectionExtensions.cs)
// aynısı, tek farkla: burada metrik yok, yalnızca iz (trace) — bir referans istemcinin ölçmesi
// gereken şey "bu çağrı nereye gitti, ne kadar sürdü" sorusudur, kaynak tüketimi değil.

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CustomerSupportBot.A2AClient.Sample.Observability;

public static class SampleTelemetry
{
    public const string ActivitySourceName = "CustomerSupportBot.A2AClient.Sample";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>
    /// "Telemetry" bölümünden okur — sunucudaki <c>TelemetryOptions</c> ile aynı adlandırma.
    /// Varsayılan KAPALI: bu örneği çalıştıran bir partnerin collector'ı olmayabilir; kapalıyken
    /// <see cref="Source"/> üzerindeki <c>StartActivity</c> çağrıları dinleyicisiz kalır ve
    /// no-op'a düşer — kod tarafında hiçbir dallanma gerekmez.
    /// </summary>
    public static IServiceCollection AddSampleTelemetry(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Telemetry");
        if (!section.GetValue("Enabled", false))
            return services;

        var serviceName = section["ServiceName"] ?? "CustomerSupportBot.A2AClient.Sample";
        var otlpEndpoint = section["Otlp:Endpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing.AddSource(ActivitySourceName).AddHttpClientInstrumentation();

                // Collector adresi verilmemişse konsola yaz — hiç iz görmemekten iyidir ve
                // sample'ı ilk kez çalıştıran birinin OTel'in gerçekten çalıştığını hemen
                // görmesini sağlar.
                if (string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddConsoleExporter();
                else
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
