// Tests/Evaluation/EvaluationModelsTests.cs

namespace CustomerSupportBot.Api.Tests.Evaluation;

public class EvaluationModelsTests
{
    // ─── ScenarioResult ───

    [Fact]
    public void ScenarioResult_Defaults_AreSafe()
    {
        var r = new ScenarioResult();
        r.ScenarioId.Should().BeEmpty();
        r.Category.Should().BeEmpty();
        r.Query.Should().BeEmpty();
        r.TraceId.Should().BeNull();
        r.PassedCriteria.Should().Be(0);
        r.TotalCriteria.Should().Be(0);
        r.CriteriaResults.Should().BeEmpty();
        r.AgentsVisited.Should().BeEmpty();
        r.ToolsCalled.Should().BeEmpty();
        r.DurationMs.Should().Be(0);
        r.Error.Should().BeNull();
    }

    [Fact]
    public void ScenarioResult_Passed_RequiresAllCriteriaPassed()
    {
        new ScenarioResult { TotalCriteria = 0, PassedCriteria = 0 }.Passed.Should().BeFalse();
        new ScenarioResult { TotalCriteria = 3, PassedCriteria = 3 }.Passed.Should().BeTrue();
        new ScenarioResult { TotalCriteria = 3, PassedCriteria = 2 }.Passed.Should().BeFalse();
        new ScenarioResult { TotalCriteria = 3, PassedCriteria = 0 }.Passed.Should().BeFalse();
    }

    [Fact]
    public void ScenarioResult_AssignableProperties_RoundTrip()
    {
        var r = new ScenarioResult
        {
            ScenarioId = "S1",
            Category = "order",
            Query = "sipariş nerede",
            TraceId = "trace-1",
            Response = "yanıt",
            TerminationReason = "completed",
            DetectedIntent = "order_inquiry",
            DurationMs = 1234,
            Error = "x"
        };
        r.AgentsVisited.Add("PlanningAgent");
        r.ToolsCalled.Add("order_status_tool");
        r.CriteriaResults.Add(new CriterionResult { Criterion = "c", Passed = true });

        r.AgentsVisited.Should().ContainSingle();
        r.ToolsCalled.Should().ContainSingle();
        r.CriteriaResults.Should().ContainSingle();
        r.DurationMs.Should().Be(1234);
    }

    // ─── EvaluationRunResult ───

    [Fact]
    public void EvaluationRunResult_Defaults_AreSafe()
    {
        var r = new EvaluationRunResult();
        r.TotalScenarios.Should().Be(0);
        r.PassedScenarios.Should().Be(0);
        r.FailedScenarios.Should().Be(0);
        r.PartialScenarios.Should().Be(0);
        r.Results.Should().BeEmpty();
        r.StartedAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void EvaluationRunResult_PassRate_ZeroTotal_IsZero()
    {
        new EvaluationRunResult { TotalScenarios = 0 }.PassRate.Should().Be(0.0);
    }

    [Theory]
    [InlineData(10, 10, 1.0)]
    [InlineData(10, 5, 0.5)]
    [InlineData(10, 0, 0.0)]
    [InlineData(4, 1, 0.25)]
    public void EvaluationRunResult_PassRate_Computed(int total, int passed, double expected)
    {
        var r = new EvaluationRunResult { TotalScenarios = total, PassedScenarios = passed };
        r.PassRate.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void EvaluationRunResult_Results_AccumulateProperly()
    {
        var r = new EvaluationRunResult();
        r.Results.Add(new ScenarioResult { ScenarioId = "A", TotalCriteria = 1, PassedCriteria = 1 });
        r.Results.Add(new ScenarioResult { ScenarioId = "B", TotalCriteria = 1, PassedCriteria = 0 });
        r.Results.Should().HaveCount(2);
        r.Results.Count(x => x.Passed).Should().Be(1);
    }

    // ─── CriterionResult ───

    [Fact]
    public void CriterionResult_Defaults()
    {
        var c = new CriterionResult();
        c.Criterion.Should().BeEmpty();
        c.Passed.Should().BeFalse();
        c.Evaluation.Should().BeEmpty();
        c.Skipped.Should().BeNull();
    }

    // ─── ScenarioFile / EvaluationScenario ───

    [Fact]
    public void EvaluationScenario_Defaults_HaveEmptyCollections()
    {
        var s = new EvaluationScenario();
        s.Id.Should().BeEmpty();
        s.Category.Should().BeEmpty();
        s.Query.Should().BeEmpty();
        s.ExpectedAgents.Should().BeEmpty();
        s.ExpectedTools.Should().BeEmpty();
        s.SuccessCriteria.Should().BeEmpty();
        s.ExpectedIntent.Should().BeNull();
        s.ExpectedBehavior.Should().BeNull();
        s.KnownFailureMode.Should().BeNull();
    }

    [Fact]
    public void ScenarioFile_Defaults_VersionAndEmptyScenarios()
    {
        var f = new ScenarioFile();
        f.Version.Should().Be(1);
        f.Scenarios.Should().BeEmpty();
    }
}
