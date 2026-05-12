// Models/ConversationPhase.cs
// SessionState.Phase için type-safe enum.
// Eski string tabanlı "greeting"/"inquiry"/"action"/"resolution" yerine.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Konuşmanın mevcut fazı.
/// SessionState.Phase string yerine bu enum kullanılır.
/// </summary>
public enum ConversationPhase
{
    /// <summary>Karşılama / ilk temas.</summary>
    Greeting,

    /// <summary>Bilgi toplama / soru sorma.</summary>
    Inquiry,

    /// <summary>Aksiyon alınıyor (sipariş, şikayet vb.).</summary>
    Action,

    /// <summary>Sonuç / çözüm fazı.</summary>
    Resolution
}
