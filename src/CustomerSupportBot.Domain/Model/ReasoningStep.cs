// Models/ReasoningStep.cs
// Structured reasoning steps.
// Daha önce steps düz string listesiydi; artık her adım yapılandırılmış object:
// Hangi aksiyon, neye dayanıyor (grounding), neden seçildi, confidence ne kadar.
// Böylece trace okuyan bir geliştirici modelin kararını (sadece metnini değil)
// Inceleyebilir ve downstream sanity check kurallarını buradan besleyebilir.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Reasoning sürecindeki tek bir yapılandırılmış adım.
/// LLM'in ürettiği plan satırlarını düz string yerine "action + grounding + confidence"
/// Olarak yakalar; böylece her adım bağımsız olarak denetlenebilir.
/// </summary>
public class ReasoningStep
{
    /// <summary>1-indexed adım sırası (stream UX'inde göstermek için).</summary>
    public int Order { get; set; }

    /// <summary>
    /// İnsanlara yönelik kısa, anlaşılır açıklama (UI'da bu gösterilir).
    /// Mutlaka dolu olmalı.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Makine okuyabilir aksiyon etiketi: "extract", "route", "clarify", "call_tool",
    /// "verify", "terminate" vb. Null veya boş olabilir.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Bu adımın geçerli olması için varsayılan önkoşul
    /// (ör. "order_id mevcut", "customer_id doğrulandı"). Null olabilir.
    /// </summary>
    public string? Premise { get; set; }

    /// <summary>
    /// Adımın dayandığı kanıt kaynağı: "regex", "session_state", "history", "DB",
    /// "derived", "assumption". Null ise model bağımsız varsayım yapmış demektir
    /// (sanity checker için kırmızı bayrak).
    /// </summary>
    public string? Grounding { get; set; }

    /// <summary>
    /// Bu adım için bağımsız güven skoru (0.0 - 1.0). Final confidenceScore
    /// Bunlardan türetilebilir (ör. min veya çarpım).
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>
    /// Aynı konumda düşünülüp reddedilen alternatif (ör. "get_all_orders_tool —
    /// Gereksiz, kullanıcı sadece son siparişi istedi"). Null olabilir.
    /// </summary>
    public string? AlternativeRejected { get; set; }
}

