// Models/ChatBridgeMessage.cs
// HITL Live Takeover — user <-> admin arası tek mesajlık paket.
// Hem history buffer'ında saklanır hem SSE event payload'ı olarak iletilir.

namespace CustomerSupportBot.Domain.Model;

public enum ChatBridgeSender
{
    /// <summary>End-user'ın chat penceresinden yazdığı mesaj.</summary>
    User,

    /// <summary>Bot workflow'unun nihai yanıtı (Human moddan ÖNCE kaydedildi).</summary>
    Bot,

    /// <summary>İnsan temsilcinin admin panelden yazdığı mesaj.</summary>
    Admin,

    /// <summary>Sistem bildirimi (temsilci katıldı, temsilci ayrıldı vb.).</summary>
    System,

    /// <summary>
    /// Bot "yazıyor…" canlı göstergesi (typing indicator). Text alanı
    /// "on" veya "off" — history'e yazılmaz, sadece broadcast edilir.
    /// </summary>
    BotTyping,

    /// <summary>
    /// Sesli görüşme sinyali (WebRTC teklif/yanıt/ağ adresi, çalma, kabul…). Text: JSON yük. Geçmişe
    /// yazılmaz ve yönlüdür: yalnızca hedef tarafa yayınlanır.
    /// </summary>
    VoiceSignal
}

public class ChatBridgeMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SessionId { get; set; } = "";
    public ChatBridgeSender Sender { get; set; } = ChatBridgeSender.User;
    public string Text { get; set; } = "";

    /// <summary>Admin mesajlarında temsilci adı.</summary>
    public string? HumanAgent { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Sesli görüşme döküm satırıysa görüşme kimliği; değilse <c>null</c>.</summary>
    public string? VoiceCallId { get; set; }

    /// <summary>Döküm satırının konuşanı: <c>agent</c> | <c>customer</c>.</summary>
    public string? VoiceTrack { get; set; }

    /// <summary>Döküm satırının görüşme başından itibaren konumu (ms) — kayıtta o ana atlamak için.</summary>
    public int? OffsetMs { get; set; }
}

