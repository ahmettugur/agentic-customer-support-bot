// EfCore/Entities/Chat/ConversationDispositionEntity.cs
// chat.conversation_dispositions — temsilcinin canlı sohbeti kapatırken kaydettiği neden/etiket/not.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;

public sealed class ConversationDispositionEntity
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? Note { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime ClosedAt { get; set; }
}
