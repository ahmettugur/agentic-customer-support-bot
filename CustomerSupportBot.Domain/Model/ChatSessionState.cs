// Models/ChatSessionState.cs
// HITL Live Takeover — bir session'ın mevcut kip durumunun snapshot'ı.
// Admin UI "Aktif Sohbetler" listesinde bu data render edilir.

namespace CustomerSupportBot.Domain.Model;

public class ChatSessionState
{
    public string SessionId { get; set; } = "";
    public ChatMode Mode { get; set; } = ChatMode.Bot;

    /// <summary>Human modda devralan temsilcinin adı (Bot'ta null).</summary>
    public string? HumanAgent { get; set; }

    /// <summary>Human moda geçiş anı.</summary>
    public DateTime? EnteredAt { get; set; }

    /// <summary>En son mesajın (user/admin/system) zaman damgası.</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>Köprüye yazılmış toplam mesaj sayısı (history dahil).</summary>
    public int MessageCount { get; set; }
}

