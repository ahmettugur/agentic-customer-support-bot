// Tur sonu token özeti: Reasoning, Planning ve her ajanın harcaması tek satırda loglanır. Biriktirici oturum kapsamından
// (AsyncLocal) hangi görüşmeye yazacağını bulur ve paralel alt görevlerden gelen kayıtlarda doğru toplamalıdır.

using CustomerSupportBot.Application.Services.Telemetry;

namespace CustomerSupportBot.Application.Tests;

public class TurnUsageTrackerTests
{
    private readonly LlmCallAttribution _attribution = new();
    private TurnUsageTracker Tracker() => new(_attribution);

    [Fact]
    public void Records_AreGroupedPerLabel_WithCallCounts()
    {
        var t = Tracker();
        t.Begin("s1");
        using (_attribution.BeginSession("s1"))
        {
            t.Record("Reasoning", 800, 120);
            t.Record("PlanningAgent", 1500, 90);
            t.Record("PlanningAgent", 500, 10);
        }

        var summary = t.Complete("s1");

        summary.Should().NotBeNull();
        summary!.Lines.Select(l => l.Label).Should().Equal("Reasoning", "PlanningAgent");
        var planning = summary.Lines.Single(l => l.Label == "PlanningAgent");
        planning.InputTokens.Should().Be(2000);
        planning.OutputTokens.Should().Be(100);
        planning.Calls.Should().Be(2);
        summary.TotalInput.Should().Be(2800);
        summary.TotalOutput.Should().Be(220);
        summary.TotalCalls.Should().Be(3);
    }

    [Fact]
    public void Record_OutsideSessionScope_IsIgnored()
    {
        var t = Tracker();
        t.Begin("s1");
        t.Record("Reasoning", 10, 10);
        t.Complete("s1").Should().BeNull();
    }

    [Fact]
    public void Record_ForSessionWithoutBegin_IsIgnored()
    {
        var t = Tracker();
        using (_attribution.BeginSession("other"))
            t.Record("Reasoning", 10, 10);
        t.Complete("other").Should().BeNull();
    }

    [Fact]
    public void Complete_RemovesTheTurn()
    {
        var t = Tracker();
        t.Begin("s1");
        using (_attribution.BeginSession("s1")) t.Record("ResponseAgent", 5, 5);
        t.Complete("s1").Should().NotBeNull();
        t.Complete("s1").Should().BeNull();
    }

    [Fact]
    public async Task ParallelRecords_FromChildTasks_AreAllCounted()
    {
        var t = Tracker();
        t.Begin("s1");
        using (_attribution.BeginSession("s1"))
        {
            await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => t.Record("ProductAgent", 3, 2))));
        }

        var line = t.Complete("s1")!.Lines.Single();
        line.Calls.Should().Be(200);
        line.InputTokens.Should().Be(600);
        line.OutputTokens.Should().Be(400);
    }

    [Fact]
    public void Format_ListsEveryLabelAndTotals_WithoutAnyUserText()
    {
        var t = Tracker();
        t.Begin("abcdef123456");
        using (_attribution.BeginSession("abcdef123456"))
        {
            t.Record("Reasoning", 800, 120);
            t.Record("ResponseAgent", 2000, 300);
        }

        var text = t.Complete("abcdef123456")!.Format("abcdef123456");

        text.Should().Contain("[Usage]").And.Contain("abcdef12");
        text.Should().Contain("Reasoning").And.Contain("ResponseAgent");
        text.Should().Contain("800").And.Contain("120").And.Contain("2800").And.Contain("420");
        text.Should().NotContain("abcdef123456");
    }
}
