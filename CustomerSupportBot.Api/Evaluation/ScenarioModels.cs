// Evaluation/ScenarioModels.cs
// Docs/evaluation-scenarios.yaml için DTO'lar ve sonuç modelleri.

using YamlDotNet.Serialization;

namespace CustomerSupportBot.Evaluation;

/// <summary>YAML kökü: version + scenarios listesi.</summary>
public class ScenarioFile
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "scenarios")]
    public List<EvaluationScenario> Scenarios { get; set; } = new();
}

/// <summary>Tek bir evaluation senaryosu.</summary>
public class EvaluationScenario
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "category")]
    public string Category { get; set; } = "";

    [YamlMember(Alias = "query")]
    public string Query { get; set; } = "";

    [YamlMember(Alias = "expected_intent")]
    public string? ExpectedIntent { get; set; }

    [YamlMember(Alias = "expected_behavior")]
    public string? ExpectedBehavior { get; set; }

    [YamlMember(Alias = "expected_agents")]
    public List<string> ExpectedAgents { get; set; } = new();

    [YamlMember(Alias = "expected_tools")]
    public List<string> ExpectedTools { get; set; } = new();

    [YamlMember(Alias = "success_criteria")]
    public List<string> SuccessCriteria { get; set; } = new();

    [YamlMember(Alias = "known_failure_mode")]
    public string? KnownFailureMode { get; set; }
}

/// <summary>Tek bir senaryonun çalıştırılma sonucu.</summary>
public class ScenarioResult
{
    public string ScenarioId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Query { get; set; } = "";
    public string? TraceId { get; set; }

    /// <summary>Toplam başarılı kriter sayısı / toplam kriter sayısı.</summary>
    public int PassedCriteria { get; set; }
    public int TotalCriteria { get; set; }

    public bool Passed => TotalCriteria > 0 && PassedCriteria == TotalCriteria;

    public List<CriterionResult> CriteriaResults { get; set; } = new();

    public string? Response { get; set; }
    public string? TerminationReason { get; set; }
    public string? DetectedIntent { get; set; }
    public List<string> AgentsVisited { get; set; } = new();
    public List<string> ToolsCalled { get; set; } = new();
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public class CriterionResult
{
    public string Criterion { get; set; } = "";
    public bool Passed { get; set; }
    public string Evaluation { get; set; } = "";
    public string? Skipped { get; set; } // Skip sebebi (e.g. "manual_review_needed")
}

/// <summary>Tüm evaluation run'ının özeti.</summary>
public class EvaluationRunResult
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; }
    public int TotalScenarios { get; set; }
    public int PassedScenarios { get; set; }
    public int FailedScenarios { get; set; }
    public int PartialScenarios { get; set; }

    public double PassRate => TotalScenarios == 0 ? 0.0 : (double)PassedScenarios / TotalScenarios;

    public List<ScenarioResult> Results { get; set; } = new();
}
