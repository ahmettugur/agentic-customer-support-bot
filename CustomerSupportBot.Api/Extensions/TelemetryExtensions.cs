// Extensions/TelemetryExtensions.cs
// Telemetri adapter'ını kayıt eder. Tüm mantık Adapters.Telemetry'e taşınmıştır.
using CustomerSupportBot.Adapters.Telemetry.DependencyInjection;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;

namespace CustomerSupportBot.Api.Extensions;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetryServices(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddTelemetryAdapters(
            configuration,
            activitySourceName: CustomerSupportTelemetry.ActivitySourceName,
            meterName: CustomerSupportTelemetry.MeterName);
}

