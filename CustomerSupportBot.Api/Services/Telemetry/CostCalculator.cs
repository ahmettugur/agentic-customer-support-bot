using CustomerSupportBot.Api.Models;
// Services/Telemetry/CostCalculator.cs
// TelemetryOptions.Pricing tablosunu okuyup, model adına göre USD maliyeti hesaplar.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Services.Telemetry;

public sealed class CostCalculator : ICostCalculator
{
    private readonly TelemetryOptions _options;

    public CostCalculator(IOptions<TelemetryOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyCollection<string> KnownModels => _options.Pricing.Keys;

    public decimal Estimate(string? model, long inputTokens, long outputTokens)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return 0m;

        TelemetryOptions.ModelPricing? pricing = null;
        if (!string.IsNullOrWhiteSpace(model) && _options.Pricing.TryGetValue(model, out var match))
        {
            pricing = match;
        }
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

