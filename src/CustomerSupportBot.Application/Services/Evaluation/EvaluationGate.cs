// Application/Services/Evaluation/EvaluationGate.cs
// Bir değerlendirme koşusunun CI'da "geçti/kaldı" kararı. Saf fonksiyon: LLM ya da I/O yok.
//
// Ham PassRate kapı için GÜVENİLİR DEĞİLDİR: manual_review kriterleri Passed=false olarak toplam kriter sayısına
// girer, dolayısıyla manuel kriteri olan senaryo asla "geçti" sayılmaz. Kapı yalnızca otomatik değerlendirilebilen
// (Skipped olmayan) kriterlere bakar; yalnızca manuel kriteri olan senaryolar "değerlendirilmedi" sayılıp orandan çıkar.

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Services.Evaluation;

public sealed class EvaluationGateOptions
{
    public const string SectionName = "EvaluationGate";

    /// <summary>Değerlendirilen senaryoların en az bu oranı geçmeli (0–1).</summary>
    public double MinPassRate { get; set; } = 0.8;

    /// <summary>
    /// En az bu kadar senaryo değerlendirilmiş olmalı. Aksi halde kapı geçmez: her şeyin atlandığı ya da hata verdiği
    /// bir koşunun "boş ama yeşil" görünmemesi için.
    /// </summary>
    public int MinEvaluatedScenarios { get; set; } = 1;

    /// <summary>Orandan bağımsız GEÇMEK ZORUNDA olan senaryolar (ör. prompt injection). Eksik/değerlendirilmemiş de kalır sayılır.</summary>
    public List<string> RequiredScenarios { get; set; } = [];
}

public static class ScenarioGateStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Error = "error";
    public const string NotEvaluated = "not_evaluated";
}

public sealed record ScenarioGateOutcome(string ScenarioId, string Status, IReadOnlyList<string> FailedCriteria, string? Error);

public sealed record GateVerdict(
    bool Passed,
    double PassRate,
    int Evaluated,
    int Passing,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ScenarioGateOutcome> Scenarios);

public static class EvaluationGate
{
    public static GateVerdict Evaluate(EvaluationRunResult run, EvaluationGateOptions options)
    {
        var outcomes = run.Results.Select(Classify).ToList();
        var evaluated = outcomes.Where(o => o.Status != ScenarioGateStatus.NotEvaluated).ToList();
        var passing = evaluated.Count(o => o.Status == ScenarioGateStatus.Passed);
        var rate = evaluated.Count == 0 ? 0d : passing / (double)evaluated.Count;

        var reasons = new List<string>();

        if (evaluated.Count == 0)
            reasons.Add("Değerlendirilen senaryo yok: otomatik kriteri olan hiçbir senaryo çalıştırılamadı.");
        else if (evaluated.Count < options.MinEvaluatedScenarios)
            reasons.Add($"Değerlendirilen senaryo sayısı {evaluated.Count}, en az {options.MinEvaluatedScenarios} gerekli.");

        if (evaluated.Count > 0 && rate < options.MinPassRate)
            reasons.Add($"Başarı oranı %{rate * 100:0} ({passing}/{evaluated.Count}), eşik %{options.MinPassRate * 100:0}.");

        foreach (var required in options.RequiredScenarios)
        {
            var outcome = outcomes.FirstOrDefault(o => o.ScenarioId.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (outcome is null)
                reasons.Add($"Zorunlu senaryo {required} koşuda yok.");
            else if (outcome.Status == ScenarioGateStatus.NotEvaluated)
                reasons.Add($"Zorunlu senaryo {required} değerlendirilemedi (otomatik kriteri yok).");
            else if (outcome.Status != ScenarioGateStatus.Passed)
                reasons.Add($"Zorunlu senaryo {required} geçmedi ({outcome.Status}).");
        }

        return new GateVerdict(reasons.Count == 0, rate, evaluated.Count, passing, reasons, outcomes);
    }

    private static ScenarioGateOutcome Classify(ScenarioResult result)
    {
        if (!string.IsNullOrEmpty(result.Error))
            return new ScenarioGateOutcome(result.ScenarioId, ScenarioGateStatus.Error, [], result.Error);

        var automated = result.CriteriaResults.Where(c => c.Skipped is null).ToList();
        if (automated.Count == 0)
            return new ScenarioGateOutcome(result.ScenarioId, ScenarioGateStatus.NotEvaluated, [], null);

        var failed = automated.Where(c => !c.Passed).Select(c => c.Criterion).ToList();
        return new ScenarioGateOutcome(
            result.ScenarioId, failed.Count == 0 ? ScenarioGateStatus.Passed : ScenarioGateStatus.Failed, failed, null);
    }
}
