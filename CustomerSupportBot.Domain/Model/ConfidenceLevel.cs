// Models/ConfidenceLevel.cs
// ReasoningResult.Confidence string yerine type-safe enum.
// "yüksek"/"orta"/"düşük" karşılaştırmaları artık enum tabanlı.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Reasoning güven seviyesi.
/// Eski string tabanlı "yüksek"/"orta"/"düşük" yerine type-safe alternatif.
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>Düşük güven (&lt;0.5).</summary>
    Low,

    /// <summary>Orta güven (0.5-0.75).</summary>
    Medium,

    /// <summary>Yüksek güven (&gt;=0.75).</summary>
    High
}

