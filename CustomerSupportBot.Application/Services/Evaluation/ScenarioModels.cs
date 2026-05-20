// Application/Services/Evaluation/ScenarioModels.cs
// Evaluation senaryoları için DTO'lar ve sonuç modelleri.

namespace CustomerSupportBot.Application.Services.Evaluation;

/// <summary>Senaryo dosyasının kök yapısı: version + scenarios listesi.</summary>
public class ScenarioFile
{
    public int Version { get; set; } = 1;
    public List<EvaluationScenario> Scenarios { get; set; } = new();
}

/// <summary>Tek bir evaluation senaryosu.</summary>
public class EvaluationScenario
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Query { get; set; } = "";
    public string? ExpectedIntent { get; set; }
    public string? ExpectedBehavior { get; set; }
    public List<string> ExpectedAgents { get; set; } = new();
    public List<string> ExpectedTools { get; set; } = new();
    public List<string> SuccessCriteria { get; set; } = new();
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
