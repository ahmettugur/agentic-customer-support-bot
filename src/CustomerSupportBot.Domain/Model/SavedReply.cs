// Domain/Model/SavedReply.cs
// Ekibin ortak hazır yanıtı — temsilci canlı sohbette mesaj kutusuna ekler.

namespace CustomerSupportBot.Domain.Model;

public sealed class SavedReply
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>İsteğe bağlı kısa ad (ör. <c>kargo-gecikme</c>); benzersizdir.</summary>
    public string? Shortcut { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
