// Application/Ports/Outbound/EvaluationQualityOptions.cs
// MEAI (Microsoft.Extensions.AI.Evaluation.Quality) LLM-judge kalite kontrollerinin
// (relevance/coherence) global açma/kapama anahtarı.

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Evaluation senaryolarındaki <c>quality_checks</c> (relevance/coherence — MEAI LLM-judge
/// evaluator'ları) için global kapı. Varsayılan <c>false</c>: her koşum gerçek bir ek LLM
/// çağrısı (judge modeli) gerektirdiği için maliyetli — senaryo bunları istese bile bu
/// <c>false</c> olduğu sürece atlanır (<c>Skipped="quality_checks_disabled"</c>). CI'da ayrı,
/// isteğe bağlı bir job'da <c>true</c> yapılması önerilir.
/// </summary>
public class EvaluationQualityOptions
{
    public const string SectionName = "EvaluationQuality";

    public bool Enabled { get; set; } = false;
}
