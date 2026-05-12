using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Telemetry;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Tests.Services.Telemetry;

public class CostCalculatorTests
{
    private static CostCalculator Build(Action<TelemetryOptions>? configure = null)
    {
        var opts = new TelemetryOptions();
        configure?.Invoke(opts);
        return new CostCalculator(Options.Create(opts));
    }

    [Fact]
    public void Estimate_NoTokens_ReturnsZero()
    {
        var calc = Build(o => o.Pricing["gpt-x"] = new TelemetryOptions.ModelPricing
        {
            InputPer1K = 0.01m,
            OutputPer1K = 0.03m
        });

        calc.Estimate("gpt-x", 0, 0).Should().Be(0m);
    }

    [Fact]
    public void Estimate_NoPricingTable_ReturnsZero()
    {
        var calc = Build();
        calc.Estimate("gpt-x", 1000, 500).Should().Be(0m);
    }

    [Fact]
    public void Estimate_KnownModel_ComputesUsd()
    {
        var calc = Build(o => o.Pricing["gpt-x"] = new TelemetryOptions.ModelPricing
        {
            InputPer1K = 0.002m,
            OutputPer1K = 0.006m
        });

        // 1000 input * 0.002 + 500 output * 0.006/1000 = 0.002 + 0.003 = 0.005
        var actual = calc.Estimate("gpt-x", 1000, 500);
        actual.Should().Be(0.005m);
    }

    [Fact]
    public void Estimate_UnknownModel_FallsBackToDefault()
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
        var actual = calc.Estimate("unknown-model", 2000, 1000);
        actual.Should().Be(0.004m);
    }

    [Fact]
    public void Estimate_ModelMatchIsCaseInsensitive()
    {
        var calc = Build(o => o.Pricing["GPT-X"] = new TelemetryOptions.ModelPricing
        {
            InputPer1K = 0.01m,
            OutputPer1K = 0m
        });

        calc.Estimate("gpt-x", 1000, 0).Should().Be(0.01m);
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
