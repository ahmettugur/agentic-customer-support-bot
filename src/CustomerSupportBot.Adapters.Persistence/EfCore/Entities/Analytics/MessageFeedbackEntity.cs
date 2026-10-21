// `analytics.message_feedback` — bot yanıtlarına 👍/👎 (anahtar: oturum + mesaj özeti).

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;

public sealed class MessageFeedbackEntity
{
    public string SessionId { get; set; } = "";
    public string MessageHash { get; set; } = "";
    public bool Helpful { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public string? TraceId { get; set; }
    public string? CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
