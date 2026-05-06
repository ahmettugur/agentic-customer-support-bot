using CustomerSupportBot.Services.Telemetry;

namespace CustomerSupportBot.Tests.Services.Telemetry;

public class CostUsageStoreTests
{
    [Fact]
    public void Record_FirstCall_PopulatesSnapshot()
    {
        var store = new CostUsageStore();
        store.Record("gpt-x", inputTokens: 1000, outputTokens: 500, costUsd: 0.005m, durationMs: 120);

        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(1);
        snap.TotalInputTokens.Should().Be(1000);
        snap.TotalOutputTokens.Should().Be(500);
        snap.TotalCostUsd.Should().Be(0.005m);
        snap.ByModel.Should().HaveCount(1);
        snap.ByModel[0].Model.Should().Be("gpt-x");
        snap.ByModel[0].Calls.Should().Be(1);
        snap.ByModel[0].AverageLatencyMs.Should().Be(120);
    }

    [Fact]
    public void Record_MultipleCalls_AggregatesPerModel()
    {
        var store = new CostUsageStore();
        store.Record("gpt-x", 1000, 500, 0.01m, 100);
        store.Record("gpt-x", 2000, 1000, 0.02m, 200);
        store.Record("claude", 500, 100, 0.001m, 50);

        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(3);
        snap.TotalInputTokens.Should().Be(3500);
        snap.TotalOutputTokens.Should().Be(1600);
        snap.TotalCostUsd.Should().Be(0.031m);
        snap.ByModel.Should().HaveCount(2);

        var gpt = snap.ByModel.Single(m => m.Model == "gpt-x");
        gpt.Calls.Should().Be(2);
        gpt.InputTokens.Should().Be(3000);
        gpt.OutputTokens.Should().Be(1500);
        gpt.CostUsd.Should().Be(0.03m);
        gpt.AverageLatencyMs.Should().Be(150); // (100+200)/2
    }

    [Fact]
    public void Record_OrdersModelsByCostDescending()
    {
        var store = new CostUsageStore();
        store.Record("cheap", 100, 100, 0.0001m, 10);
        store.Record("expensive", 100, 100, 1.0m, 10);
        store.Record("medium", 100, 100, 0.05m, 10);

        var snap = store.GetSnapshot();
        snap.ByModel.Select(m => m.Model).Should().Equal("expensive", "medium", "cheap");
    }

    [Fact]
    public void Record_BlankModelName_StoredAsUnknown()
    {
        var store = new CostUsageStore();
        store.Record("", 100, 100, 0.001m, 10);
        store.Record("   ", 100, 100, 0.001m, 10);

        var snap = store.GetSnapshot();
        snap.ByModel.Should().HaveCount(1);
        snap.ByModel[0].Model.Should().Be("(unknown)");
        snap.ByModel[0].Calls.Should().Be(2);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var store = new CostUsageStore();
        store.Record("gpt-x", 1000, 500, 0.01m, 100);
        store.Reset();

        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(0);
        snap.TotalInputTokens.Should().Be(0);
        snap.TotalOutputTokens.Should().Be(0);
        snap.TotalCostUsd.Should().Be(0m);
        snap.ByModel.Should().BeEmpty();
    }

    [Fact]
    public void Record_IsThreadSafe()
    {
        var store = new CostUsageStore();
        const int iterations = 500;

        Parallel.For(0, iterations, _ =>
            store.Record("gpt-x", 10, 5, 0.001m, 50));

        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(iterations);
        snap.TotalInputTokens.Should().Be(iterations * 10);
        snap.TotalOutputTokens.Should().Be(iterations * 5);
        snap.ByModel.Single().Calls.Should().Be(iterations);
    }
}
