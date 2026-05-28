using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Telemetry cost endpoint'lerinin kullandığı primary port.
/// </summary>
public interface ITelemetryPort
{
    CostUsageSnapshot GetCostSnapshot();
    IReadOnlyCollection<string> GetKnownModels();
    void ResetCostSnapshot();
}
