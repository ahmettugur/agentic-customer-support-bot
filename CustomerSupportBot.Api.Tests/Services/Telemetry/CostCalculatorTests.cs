using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Telemetry;

public class CostCalculatorTests
{
    private static CostCalculator Build(Action<TelemetryOptions>? configure = null)
    {
        var opts = new TelemetryOptions();
        configure?.Invoke(opts);
        return new CostCalculator(Options.Create(opts));
    }

    [Fact]
    public void CalculateCost_NoTokens_ReturnsZero()
    {
        var calc = Build(o => o.Pricing["gpt-x"] = new TelemetryOptions.ModelPricing
        {
            InputPer1K = 0.01m,
            OutputPer1K = 0.03m
        });

        calc.CalculateCost("gpt-x", "", 0, 0).Should().Be(0m);
    }

    [Fact]
    public void CalculateCost_NoPricingTable_ReturnsZero()
    {
        var calc = Build();
        calc.CalculateCost("gpt-x", "", 1000, 500).Should().Be(0m);
    }

    [Fact]
    public void CalculateCost_KnownModel_ComputesUsd()
    {
        var calc = Build(o => o.Pricing["gpt-x"] = new TelemetryOptions.ModelPricing
        {
            InputPer1K = 0.002m,
            OutputPer1K = 0.006m
        });

        // 1000 input * 0.002 + 500 output * 0.006/1000 = 0.002 + 0.003 = 0.005
        var actual = calc.CalculateCost("gpt-x", "", 1000, 500);
        actual.Should().Be(0.005m);
    }

    [Fact]
    public void CalculateCost_UnknownModel_FallsBackToDefault()
    {
        var calc = Build(o =>
        {
            o.Pricing["default"] = new TelemetryOptions.ModelPricing
            {
                InputPer1K = 0.001m,
                OutputPer1K = 0.002m
            };
        });

        // 2000 input * 0.001/1000 + 1000 output * 0.002/1000 = 0.002 + 0.002 = 0.004
        var actual = calc.CalculateCost("unknown-model", "", 2000, 1000);
        actual.Should().Be(0.004m);
    }

    [Fact]
    public void CalculateCost_ProviderFallback_UsesProviderDefault()
    {
        var calc = Build(o =>
        {
            o.Pricing["azure_default"] = new TelemetryOptions.ModelPricing
            {
                InputPer1K = 0.01m,
                OutputPer1K = 0.02m
            };
        });

        var actual = calc.CalculateCost("unknown-model", "azure", 1000, 1000);
        actual.Should().Be(0.03m); // 0.01 + 0.02
    }

    [Fact]
    public void KnownModels_ReturnsConfiguredKeys()
    {
        var calc = Build(o =>
        {
            o.Pricing["gpt-a"] = new();
            o.Pricing["gpt-b"] = new();
        });

        calc.KnownModels.Should().BeEquivalentTo(new[] { "gpt-a", "gpt-b" });
    }
}
