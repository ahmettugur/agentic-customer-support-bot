// Application/Ports/Outbound/WorkflowCheckpointingOptions.cs
// Workflow checkpoint'leme ve çökme sonrası devam ayarları.

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Açıkken her workflow turu adım adım kalıcı checkpoint yazar; süreç tur ortasında ölürse (çökme, deploy, OOM)
/// başka bir tarama bu turu kaldığı yerden bitirir ve yanıt konuşma geçmişine yazılır.
///
/// <para>
/// <b>Varsayılan KAPALI:</b> her tur adım başına bir DB yazması ekler (sıcak yol) ve nadir bir durum için çalışır.
/// Açmadan önce docs/operations.md "Workflow checkpoint" bölümüne bakın.
/// </para>
/// </summary>
public class WorkflowCheckpointingOptions
{
    public const string SectionName = "WorkflowCheckpointing";

    public bool Enabled { get; set; }

    /// <summary>Devam taramasının sıklığı.</summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Bir turun "sahibi öldü" sayılması için, workflow zaman aşımına (<c>WorkflowGuards:TimeoutSeconds</c>) eklenen
    /// pay. Yaşayan bir tur zaman aşımında biter ve checkpoint'lerini siler; bu süreden sonra hâlâ duran checkpoint
    /// sahipsizdir. Devam gecikmesi de buna eşittir.
    /// </summary>
    public int GraceSeconds { get; set; } = 60;

    /// <summary>Bundan eski turlar devam ettirilmez, yalnızca silinir (müşteri çoktan gitmiştir; maliyet boşa gider).</summary>
    public int MaxAgeMinutes { get; set; } = 30;

    /// <summary>Tek taramada devam ettirilecek en fazla tur (LLM maliyeti ve yük için üst sınır).</summary>
    public int MaxTurnsPerSweep { get; set; } = 3;
}
