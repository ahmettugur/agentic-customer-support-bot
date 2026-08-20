// Application/Services/ParallelExecutionOptions.cs
// Compound query alt görevleri için paralel çalıştırma ayarları.
//
// Paralel çalıştırma yalnızca bütün tool'ları salt-okunur olan ajanlara açılır. LLM'in ürettiği
// intent, karma bir ajanın hangi tool'u gerçekten çağıracağını garanti etmez; OrderAgent gibi
// hem okuma hem yazma tool'u taşıyan ajanlar bu nedenle her zaman sıralı çalışır.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

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

    /// <summary>Tek bir compound sorguda kabul edilen en yüksek alt görev sayısı.</summary>
    public int MaxSubTasks { get; set; } = 6;

    /// <summary>Tüm alt görevlerin paylaştığı uçtan uca zaman bütçesi.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Bir alt görevin yan-etkisiz olarak değerlendirilip paralel batch'e alınabileceğini
    /// belirler. Yalnızca bütün tool'ları salt-okunur olan ajanlar paralel çalışabilir.
    /// </summary>
    public bool IsReadOnly(SubTask sub)
    {
        if (string.IsNullOrWhiteSpace(sub.TargetAgent)) return false;

        return WellKnown.AgentNames.ReadOnly.Contains(sub.TargetAgent);
    }
}
