// Domain/Model/SessionAnalytics.cs
// Tek bir oturum için detaylı analytics DTO'ları.

namespace CustomerSupportBot.Domain.Model;

public class SessionAnalytics
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public int MessageCount { get; set; }
    public int TurnCount { get; set; }

    public string? CurrentIntent { get; set; }
    public string? Phase { get; set; }
    public string? CustomerId { get; set; }

    public string? Sentiment { get; set; }
    public double SentimentScore { get; set; }
    public int ConsecutiveNegativeTurns { get; set; }
    public List<SentimentTimelineEntry> SentimentTimeline { get; set; } = new();

    public SessionRatingInfo? Rating { get; set; }

    public int TotalApprovals { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public List<ApprovalSummary> ApprovalDetails { get; set; } = new();

    public int TotalEscalations { get; set; }
    public int OpenEscalations { get; set; }
    public int ResolvedEscalations { get; set; }
    public List<EscalationSummary> EscalationDetails { get; set; } = new();

    public Dictionary<string, string> CollectedInfo { get; set; } = new();
}

public class SentimentTimelineEntry
{
    public int Turn { get; set; }
    public string Label { get; set; } = "";
    public double Score { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SessionRatingInfo
{
    public int Stars { get; set; }
    public string? Feedback { get; set; }
    public DateTime RatedAt { get; set; }
}

public class ApprovalSummary
{
    public string Id { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}

public class EscalationSummary
{
    public string Id { get; set; } = "";
    public string? AgentName { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Resolution { get; set; }
}
