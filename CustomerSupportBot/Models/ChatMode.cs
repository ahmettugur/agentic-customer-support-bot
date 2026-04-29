// Models/ChatMode.cs
// HITL Live Takeover — bir session'ın işlenme kipi.
// Bot = workflow çalışır; Human = workflow atlanır, mesajlar admin ile köprüden.

namespace CustomerSupportBot.Models;

public enum ChatMode
{
    /// <summary>Normal ajan workflow'u çalışır.</summary>
    Bot,

    /// <summary>Bir insan temsilci devralmış; mesajlar admin ile köprülenir.</summary>
    Human
}
