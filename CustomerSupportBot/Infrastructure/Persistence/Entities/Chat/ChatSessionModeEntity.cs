// Infrastructure/Persistence/Entities/Chat/ChatSessionModeEntity.cs
// `chat.session_modes` tablosu — Live Takeover kip kayıtları.
// ChatMode enum'unun string karşılığı: "Bot" | "Human".

namespace CustomerSupportBot.Infrastructure.Persistence.Entities.Chat;

public sealed class ChatSessionModeEntity
{
    public string SessionId { get; set; } = "";
    public string Mode { get; set; } = "Bot";
    public string? HumanAgent { get; set; }
    public DateTime? EnteredAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int MessageCount { get; set; }
}
