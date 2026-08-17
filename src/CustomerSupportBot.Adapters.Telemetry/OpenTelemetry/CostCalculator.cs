// Adapters.Telemetry/OpenTelemetry/CostCalculator.cs
// DRIVEN ADAPTER — ICostCalculatorPort → TelemetryOptions.Pricing tabanlı maliyet hesaplama.
// Model adına göre USD/1K token fiyatlandırması yapar.

using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;

/// <summary>
/// LLM token maliyeti hesaplayan adapter.
/// TelemetryOptions.Pricing tablosundan model fiyatlarını okur.
/// </summary>
public sealed class CostCalculator : ICostCalculatorPort
{
    private readonly TelemetryOptions _options;

    public CostCalculator(IOptions<TelemetryOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyCollection<string> KnownModels => _options.Pricing.Keys;

    public decimal CalculateCost(string modelHint, string provider, int inputTokens, int outputTokens)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return 0m;

        TelemetryOptions.ModelPricing? pricing = null;

        // Model adına göre fiyat bul
        if (!string.IsNullOrWhiteSpace(modelHint) && _options.Pricing.TryGetValue(modelHint, out var match))
        {
            pricing = match;
        }
        // Provider'a göre default bul
        else if (!string.IsNullOrWhiteSpace(provider) && _options.Pricing.TryGetValue($"{provider}_default", out var providerDefault))
        {
            pricing = providerDefault;
        }
        // Global default
        else if (_options.Pricing.TryGetValue("default", out var fallback))
        {
            pricing = fallback;
        }

        if (pricing == null) return 0m;

        var inputCost = (decimal)inputTokens / 1000m * pricing.InputPer1K;
        var outputCost = (decimal)outputTokens / 1000m * pricing.OutputPer1K;
        return inputCost + outputCost;
    }
}
