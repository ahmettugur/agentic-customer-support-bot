// Models/ParallelExecutionOptions.cs
// Compound query alt görevleri için paralel çalıştırma ayarları.
// Hangi agent'ların read-only (yan-etkisiz) olduğu WellKnown.AgentNames.ReadOnly
// üzerinden belirlenir — config'de ayrı bir liste tutulmaz.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Bileşik (compound) sorgudaki alt görevlerin paralel yürütme politikası.
/// </summary>
public class ParallelExecutionOptions
{
    public const string SectionName = "ParallelExecution";

    /// <summary>Paralel sub-task çalıştırma aktif mi?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Aynı anda en fazla kaç yan-etkisiz alt görev çalıştırılabilir.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Bir alt görevin yan-etkisiz olarak değerlendirilip paralel batch'e
    /// alınabileceğini belirler. Kaynak: <see cref="WellKnown.AgentNames.ReadOnly"/>.
    /// </summary>
    public bool IsReadOnly(SubTask sub)
    {
        if (string.IsNullOrWhiteSpace(sub.TargetAgent)) return false;
        return WellKnown.AgentNames.ReadOnly.Contains(sub.TargetAgent);
    }
}
