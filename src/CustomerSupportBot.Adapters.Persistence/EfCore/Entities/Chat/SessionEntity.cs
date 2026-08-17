// Infrastructure/Persistence/Entities/Chat/SessionEntity.cs
// `chat.sessions` tablosu — AgentSession domain modelinin DB karşılığı.
// State alanı JSONB (string) olarak tutulur; mapper SessionState'e çevirir.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;

public sealed class SessionEntity
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }

    /// <summary>SessionState JSONB. Mapper SerDe yapar.</summary>
    public string StateJson { get; set; } = "{}";
}
