// Models/EscalationAction.cs
// Eskalasyon karar aksiyonları — InMemoryEscalationSink.Decide parametresi.
// String karşılaştırma yerine type-safe enum.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Eskalasyon kaydına uygulanabilecek aksiyonlar.
/// Eski string tabanlı "acknowledge"/"resolve"/"dismiss" yerine type-safe alternatif.
/// </summary>
public enum EscalationAction
{
    /// <summary>Temsilci aldı ama henüz çözmedi.</summary>
    Acknowledge,

    /// <summary>Sorun çözüldü.</summary>
    Resolve,

    /// <summary>Geçersiz bulundu (yanlış eskalasyon).</summary>
    Dismiss
}
