// Models/ParallelExecutionOptions.cs
// Compound query alt görevleri için paralel çalıştırma ayarları.
// Sadece yan etkisiz (read-only) specialist'ler paralel batch'e dahil olur:
// ProductInquiryAgent, OrderInquiryAgent. OrderPlacementAgent ve ComplaintAgent
// HITL gate'i ve sıralı state etkisi nedeniyle her zaman serial kalır.

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
    /// Read-only kabul edilen specialist agent isimleri. Bu listede yer alan
    /// agent'lara giden alt görevler ardı ardına gelirse paralelleştirilebilir.
    /// </summary>
    public List<string> ReadOnlyAgents { get; set; } = new()
    {
        WellKnown.AgentNames.ProductInquiry,
        WellKnown.AgentNames.OrderInquiry
    };

    /// <summary>
    /// Bir alt görevin yan-etkisiz olarak değerlendirilip paralel batch'e
    /// alınabileceğini belirler.
    /// </summary>
    public bool IsReadOnly(SubTask sub)
    {
        if (string.IsNullOrWhiteSpace(sub.TargetAgent)) return false;
        return ReadOnlyAgents.Any(a =>
            string.Equals(a, sub.TargetAgent, StringComparison.OrdinalIgnoreCase));
    }
}
