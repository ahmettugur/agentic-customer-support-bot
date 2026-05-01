// Infrastructure/Persistence/Entities/Chat/MessageEntity.cs
// `chat.messages` tablosu — sıralı user/assistant mesaj geçmişi.
// AppendAssistantMessage davranışı için son satır UPDATE/INSERT mantığı
// PostgresSessionManager'da uygulanır; entity sadece veriyi taşır.

namespace CustomerSupportBot.Infrastructure.Persistence.Entities.Chat;

public sealed class MessageEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string Role { get; set; } = "";       // "user" | "assistant"
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
