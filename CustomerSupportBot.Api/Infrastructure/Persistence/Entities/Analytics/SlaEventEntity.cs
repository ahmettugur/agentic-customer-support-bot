// Infrastructure/Persistence/Entities/Analytics/SlaEventEntity.cs
// `analytics.sla_events` tablosu — SLA Guardian'ın ürettiği warn/breach olayları.

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Analytics;

public sealed class SlaEventEntity
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Kind { get; set; } = "";
    public string Severity { get; set; } = "";
    public string TargetId { get; set; } = "";
    public int AgeSeconds { get; set; }
    public string? Action { get; set; }
    public string? Note { get; set; }
}
