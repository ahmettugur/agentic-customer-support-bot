// `voice.calls` — temsilci–müşteri sesli görüşmeleri.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;

public sealed class VoiceCallEntity
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentDisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public DateTime? LastChunkAt { get; set; }
}
