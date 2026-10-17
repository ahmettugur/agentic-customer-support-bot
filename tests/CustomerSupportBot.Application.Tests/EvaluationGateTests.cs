// Değerlendirme kapısı: bir eval koşusunun CI'da "geçti/kaldı" kararı.
//
// Neden ham PassRate kullanılmıyor: manual_review kriterleri Passed=false ve toplam kriter sayısına dahil, yani
// herhangi bir manuel kriteri olan senaryo ASLA "geçti" sayılmaz (23 senaryonun çoğu). Kapı yalnızca OTOMATİK
// değerlendirilebilen (Skipped olmayan) kriterlere bakar ve "hiçbir şey değerlendirilmedi" durumunu yeşil saymaz.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Evaluation;

namespace CustomerSupportBot.Application.Tests;

public class EvaluationGateTests
{
    private static CriterionResult Ok(string name = "c") => new() { Criterion = name, Passed = true, Evaluation = "ok" };
    private static CriterionResult Bad(string name = "c") => new() { Criterion = name, Passed = false, Evaluation = "kötü" };
    private static CriterionResult Manual() => new() { Criterion = "manual_review: x", Passed = false, Skipped = "manual_review_needed" };

    private static ScenarioResult Scenario(string id, params CriterionResult[] criteria) =>
        new() { ScenarioId = id, CriteriaResults = [.. criteria], TotalCriteria = criteria.Length, PassedCriteria = criteria.Count(c => c.Passed) };

    private static EvaluationRunResult Run(params ScenarioResult[] results) => new() { TotalScenarios = results.Length, Results = [.. results] };

    private static EvaluationGateOptions Opts(double min = 0.8, int minEvaluated = 1, params string[] required) =>
        new() { MinPassRate = min, MinEvaluatedScenarios = minEvaluated, RequiredScenarios = [.. required] };

    [Fact]
    public void AllEvaluableScenariosPass_GatePasses()
    {
        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok()), Scenario("S2", Ok(), Ok())), Opts());

        Assert.True(verdict.Passed);
        Assert.Equal(1.0, verdict.PassRate);
        Assert.Equal(2, verdict.Evaluated);
    }

    [Fact]
    public void ManualReviewCriteria_AreIgnored_SoAScenarioWithOnePassingAutomatedCriterionPasses()
    {
        var scenario = Scenario("S1", Ok(), Manual());

        Assert.False(scenario.Passed, "ham Passed manuel kriter yüzünden false");
        var verdict = EvaluationGate.Evaluate(Run(scenario), Opts());

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void ScenarioWithOnlyManualCriteria_IsNotEvaluated_AndExcludedFromTheRate()
    {
        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok()), Scenario("S2", Manual(), Manual())), Opts());

        Assert.Equal(1, verdict.Evaluated);
        Assert.Equal(1.0, verdict.PassRate);
        Assert.Equal("not_evaluated", verdict.Scenarios.Single(s => s.ScenarioId == "S2").Status);
    }

    [Fact]
    public void PassRateBelowTheThreshold_FailsWithAReason()
    {
        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok()), Scenario("S2", Bad("tool_not_called"))), Opts(min: 0.8));

        Assert.False(verdict.Passed);
        Assert.Equal(0.5, verdict.PassRate);
        Assert.Contains(verdict.Reasons, r => r.Contains("%50") || r.Contains("0,5") || r.Contains("0.5"));
        Assert.Equal(["tool_not_called"], verdict.Scenarios.Single(s => s.ScenarioId == "S2").FailedCriteria);
    }

    [Fact]
    public void PassRateExactlyAtTheThreshold_Passes()
    {
        var verdict = EvaluationGate.Evaluate(
            Run(Scenario("S1", Ok()), Scenario("S2", Ok()), Scenario("S3", Ok()), Scenario("S4", Ok()), Scenario("S5", Bad())), Opts(min: 0.8));

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void NothingEvaluated_Fails_InsteadOfSilentlyGoingGreen()
    {
        var onlyManual = EvaluationGate.Evaluate(Run(Scenario("S1", Manual())), Opts());
        var empty = EvaluationGate.Evaluate(Run(), Opts());

        Assert.False(onlyManual.Passed);
        Assert.False(empty.Passed);
        Assert.Contains(empty.Reasons, r => r.Contains("Değerlendirilen senaryo yok"));
    }

    [Fact]
    public void FewerEvaluatedScenariosThanTheMinimum_Fails()
    {
        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok()), Scenario("S2", Ok())), Opts(minEvaluated: 5));

        Assert.False(verdict.Passed);
    }

    [Fact]
    public void ErroredScenario_CountsAsFailing_EvenIfItHasNoCriteria()
    {
        var errored = new ScenarioResult { ScenarioId = "S2", Error = "OpenAI 401" };

        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok()), errored), Opts(min: 0.8));

        Assert.False(verdict.Passed);
        var outcome = verdict.Scenarios.Single(s => s.ScenarioId == "S2");
        Assert.Equal("error", outcome.Status);
        Assert.Equal("OpenAI 401", outcome.Error);
    }

    [Fact]
    public void RequiredScenarioFailing_FailsTheGate_EvenWhenTheRateIsHigh()
    {
        var results = Enumerable.Range(1, 9).Select(i => Scenario($"S{i}", Ok())).Append(Scenario("S17", Bad("tool_not_called"))).ToArray();

        var verdict = EvaluationGate.Evaluate(Run(results), Opts(min: 0.8, required: "S17"));

        Assert.Equal(0.9, verdict.PassRate);
        Assert.False(verdict.Passed);
        Assert.Contains(verdict.Reasons, r => r.Contains("S17"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("manual-only")]
    public void RequiredScenarioThatWasNotEvaluated_FailsTheGate(string situation)
    {
        var results = situation == "missing"
            ? Run(Scenario("S1", Ok()))
            : Run(Scenario("S1", Ok()), Scenario("S17", Manual()));

        var verdict = EvaluationGate.Evaluate(results, Opts(required: "S17"));

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.Reasons, r => r.Contains("S17"));
    }

    [Fact]
    public void RequiredScenarioIds_AreMatchedCaseInsensitively()
    {
        var verdict = EvaluationGate.Evaluate(Run(Scenario("S17", Ok())), Opts(required: "s17"));

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void SkippedQualityChecks_DoNotCountAgainstTheScenario()
    {
        var skippedQuality = new CriterionResult { Criterion = "quality_relevance", Passed = false, Skipped = "quality_checks_disabled" };

        var verdict = EvaluationGate.Evaluate(Run(Scenario("S1", Ok(), skippedQuality)), Opts());

        Assert.True(verdict.Passed);
    }
}
