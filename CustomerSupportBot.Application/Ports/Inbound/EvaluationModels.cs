// Ports/Driving/EvaluationModels.cs
// IEvaluationPort sözleşme tipleri — senaryo DTO'ları ve sonuç modelleri.

namespace CustomerSupportBot.Application.Ports.Inbound;

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

    /// <summary>
    /// Argüman-seviyeli tool çağrı beklentisi — <c>type: tool_call_args_match</c> kriteri için.
    /// <see cref="ExpectedTools"/>'tan bağımsız: o sadece isim listesi (no_extra_tool_calls için),
    /// bu ise isim+argüman eşleşmesi (subset match — fazladan argüman sorun değil) ister.
    /// </summary>
    public List<ExpectedToolCallSpec> ExpectedToolCalls { get; set; } = new();

    public List<CriterionSpec> SuccessCriteria { get; set; } = new();
    public string? KnownFailureMode { get; set; }
}

/// <summary>YAML'da <c>expected_tool_calls</c> altında tanımlanan tek bir tool çağrı beklentisi.</summary>
public class ExpectedToolCallSpec
{
    public string Name { get; set; } = "";

    /// <summary>Null ise sadece isim kontrol edilir (argümanlara bakılmaz).</summary>
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Yapılandırılmış (typed) başarı kriteri — CriteriaEvaluator'daki dispatch table'ın anahtarı
/// olan Type dışındaki alanlar kriter türüne göre kullanılır/yoksayılır.
/// </summary>
public class CriterionSpec
{
    /// <summary>Dispatch anahtarı: contains_any, tool_called, turn_count, manual_review vb.</summary>
    public string Type { get; set; } = "";

    /// <summary>contains_any / tool_called / tool_not_called için değer listesi.</summary>
    public List<string>? Values { get; set; }

    /// <summary>turn_count / iteration_count için karşılaştırma operatörü ("&lt;=", "==", ...).</summary>
    public string? Op { get; set; }

    /// <summary>turn_count / iteration_count için eşik değer.</summary>
    public int? Value { get; set; }

    /// <summary>agent_requests_field için beklenen alan adı (ör. "customer_id").</summary>
    public string? Field { get; set; }

    /// <summary>manual_review için gözden geçirme notu.</summary>
    public string? Note { get; set; }
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
