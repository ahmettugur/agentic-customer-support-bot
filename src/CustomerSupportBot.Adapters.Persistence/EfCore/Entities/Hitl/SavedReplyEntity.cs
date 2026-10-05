// EfCore/Entities/Hitl/SavedReplyEntity.cs
// `hitl.saved_replies` — ekibin ortak hazır yanıtları.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;

public sealed class SavedReplyEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Normalleştirilmiş (küçük harf) kısayol; benzersiz.</summary>
    public string? Shortcut { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
