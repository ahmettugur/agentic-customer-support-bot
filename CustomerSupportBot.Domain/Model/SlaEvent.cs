// Models/SlaEvent.cs
// SLA Guardian domain olayı.

namespace CustomerSupportBot.Domain.Model;

/// <summary>SLA Guardian'ın ürettiği breach/warn olayı.</summary>
public class SlaEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>"approval" | "escalation".</summary>
    public string Kind { get; set; } = "";

    /// <summary>"warn" | "breach".</summary>
    public string Severity { get; set; } = "";

    public string TargetId { get; set; } = "";
    public int AgeSeconds { get; set; }
    public string? Action { get; set; }
    public string? Note { get; set; }
}

