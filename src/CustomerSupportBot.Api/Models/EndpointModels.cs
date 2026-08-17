// Api/Models/EndpointModels.cs
// Endpoint'lere özgü HTTP input DTO'ları.

namespace CustomerSupportBot.Api.Models;

// ─── AgentsEndpoints ───
public class RerouteInput
{
    public string? AgentId { get; set; }
    public string? Reason { get; set; }
}

// ─── ImprovementsEndpoints ───
public sealed record ImprovementDecision(string? DecidedBy, string? Reason);

// ─── PersonalizationEndpoints ───
public sealed class AdminNoteInput
{
    public string? Note { get; set; }
}

// ─── AnalyticsEndpoints ───
public sealed record RatingInput(int Stars, string? Feedback);
