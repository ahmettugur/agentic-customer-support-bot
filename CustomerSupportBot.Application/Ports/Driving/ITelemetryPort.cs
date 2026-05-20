// Ports/Driving/ITelemetryPort.cs
// PRIMARY PORT — telemetry/cost görünümü.

using CustomerSupportBot.Application.Ports.Driven.Observability;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Telemetry cost endpoint'lerinin kullandığı primary port.
/// </summary>
public interface ITelemetryPort
{
    CostUsageSnapshot GetCostSnapshot();
    IReadOnlyCollection<string> GetKnownModels();
    void ResetCostSnapshot();
}
