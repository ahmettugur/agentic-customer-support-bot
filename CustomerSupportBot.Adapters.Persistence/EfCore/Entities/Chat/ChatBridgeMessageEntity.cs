// Infrastructure/Persistence/Entities/Chat/ChatBridgeMessageEntity.cs
// `chat.bridge_messages` tablosu — User/Bot/Admin/System mesajlarının kalıcı kaydı.
// BotTyping (transient) DB'ye yazılmaz; sadece in-memory broadcast olur.
// Sender alanı: ChatBridgeSender enum'unun string karşılığı.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;

public sealed class ChatBridgeMessageEntity
{
    public long Id { get; set; }

    /// <summary>Domain model'in 12 karakterli Id'si — UI tarafındaki mesaj eşleştirmesi için.</summary>
    public string MessageId { get; set; } = "";

    public string SessionId { get; set; } = "";
    public string Sender { get; set; } = "";         // User | Bot | Admin | System
    public string? HumanAgent { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
