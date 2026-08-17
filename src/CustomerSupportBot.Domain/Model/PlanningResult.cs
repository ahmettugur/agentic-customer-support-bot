// Models/PlanningResult.cs
// , 1.3, 1.4 — PlanningAgent'ın yapılandırılmış çıktı modeli.
// JSON olarak parse edilip routing kararlarında kullanılır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// PlanningAgent'ın yapılandırılmış çıktısı — routing kararı (seçilen ajan,
/// reddedilen alternatifler, clarification ihtiyacı).
/// Intent tespiti bu modelde YOKTUR: intent'in tek sahibi ReasoningService'tir
/// (<see cref="ReasoningResult.Intent"/>); PlanningAgent reasoning hint'indeki
/// intent'i nihai karar kabul edip yalnızca planlama/routing yapar.
/// </summary>
public class PlanningResult
{
    /// <summary>Routing kararının dayandığı kanıtlar (kullanıcı metninden alıntılar).</summary>
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

