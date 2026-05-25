// Models/PlanningResult.cs
// , 1.3, 1.4 — PlanningAgent'ın yapılandırılmış çıktı modeli.
// JSON olarak parse edilip routing kararlarında kullanılır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// PlanningAgent'ın yapılandırılmış çıktısı — niyet analizi, seçilen ajan,
/// Reddedilen alternatifler ve clarification ihtiyacı.
/// </summary>
public class PlanningResult
{
    /// <summary>Algılanan niyet.</summary>
    public string DetectedIntent { get; set; } = "";

    /// <summary>Niyet güven skoru 0.0 - 1.0.</summary>
    public double IntentConfidence { get; set; } = 0.5;

    /// <summary>Niyetin dayandığı kanıtlar (kullanıcı metninden alıntılar).</summary>
    public List<string> SupportingEvidence { get; set; } = new();

    /// <summary>Seçilen ajan (ör. "ProductAgent").</summary>
    public string SelectedAgent { get; set; } = "";

    /// <summary>Bu ajanın seçilme gerekçesi.</summary>
    public string Rationale { get; set; } = "";

    /// <summary>Reddedilen alternatifler — hangi ajan neden seçilmedi.</summary>
    public List<RejectedAlternative> AlternativesRejected { get; set; } = new();

    /// <summary>
    /// Clarification gerekli mi? true ise specialist çağrılmaz,
    /// Kullanıcıya netleştirme sorusu yöneltilir.
    /// </summary>
    public bool NeedsClarification { get; set; }

    /// <summary>Kullanıcıya sorulacak netleştirme sorusu.</summary>
    public string? ClarificationQuestion { get; set; }

    /// <summary>Selected agent için görev açıklaması.</summary>
    public string TaskDescription { get; set; } = "";
}

/// <summary>
/// Reddedilen bir alternatif ajan — hangi ajan neden seçilmedi.
/// </summary>
public class RejectedAlternative
{
    public string Agent { get; set; } = "";
    public string Reason { get; set; } = "";
}

