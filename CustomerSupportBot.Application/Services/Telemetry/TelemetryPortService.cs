// Application/Services/TelemetryPortService.cs
// DRIVING PORT IMPL — ITelemetryPort → telemetry maliyet görünümü.

using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Services.Telemetry;

public sealed class TelemetryPortService : ITelemetryPort
{
    private readonly ICostUsageStorePort _usageStore;
    private readonly ICostCalculatorPort _calculator;

    public TelemetryPortService(ICostUsageStorePort usageStore, ICostCalculatorPort calculator)
    {
        _usageStore = usageStore;
        _calculator = calculator;
    }

    public CostUsageSnapshot GetCostSnapshot() => _usageStore.GetUsageSnapshot();

    public IReadOnlyCollection<string> GetKnownModels() => _calculator.KnownModels;

    public void ResetCostSnapshot() => _usageStore.ResetUsage();
}
