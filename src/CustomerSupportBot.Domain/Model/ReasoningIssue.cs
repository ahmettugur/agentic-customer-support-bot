// Models/ReasoningIssue.cs
// Deterministic sanity checker çıktısı.
// LLM çağrısı olmadan reasoning sonucundaki mantık tutarsızlıklarını yakalar:
//   - confidence ile nextAction uyumu var mı?
//   - requiredInfo'da zaten bilinen bir alan var mı?
//   - intent ile seçilen agent çelişiyor mu?
//
// Bulunan her tutarsızlık ReasoningIssue olarak kaydedilir; ReasoningResult'a
// Ve trace'e yansır. Frontend debug panel'inde görüntülenir.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Sanity checker'ın bulduğu tutarsızlık kaydı.
/// </summary>
public class ReasoningIssue
{
    /// <summary>
    /// Makine okuyabilir kod (snake_case). Ör. "overconfident_clarification",
    /// "redundant_required_info", "intent_action_mismatch".
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>Önem seviyesi.</summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>İnsanlara yönelik Türkçe açıklama.</summary>
    public string Message { get; set; } = "";

    /// <summary>Hangi alanı işaret ediyor (ör. "nextAction", "requiredInfo[0]").</summary>
    public string? Field { get; set; }

    /// <summary>
    /// Modele/geliştiriciye öneri: bu tutarsızlığı nasıl çözerim?
    /// Opsiyonel.
    /// </summary>
    public string? SuggestedFix { get; set; }
}

/// <summary>Tutarsızlık önem seviyeleri.</summary>
public enum IssueSeverity
{
    /// <summary>Bilgilendirme — davranış değiştirmeyebilir.</summary>
    Info,
    /// <summary>Uyarı — kalite düşer ama bozmaz.</summary>
    Warn,
    /// <summary>Hata — ping-pong veya hallucination riski yüksek.</summary>
    Error
}

