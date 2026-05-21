namespace CustomerSupportBot.Application.Ports.Driven.Observability;

/// <summary>
/// OpenTelemetry ActivitySource ve Meter isimleri — tek kaynak noktası.
/// Adapter'lar bu sabitlerden OTel kaynaklarını oluşturur.
/// </summary>
public static class TelemetryConstants
{
    public const string ActivitySourceName = "CustomerSupportBot.Api";
    public const string MeterName = "CustomerSupportBot.Api";
}
