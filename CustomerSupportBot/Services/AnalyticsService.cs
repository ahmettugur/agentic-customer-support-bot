// Services/AnalyticsService.cs
// Tüm in-memory depolardan veri toplayarak analytics dashboard özeti üretir.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

public class AnalyticsService(
    ISessionManager sessions,
    IApprovalQueue approvals,
    IEscalationSink escalations,
    IRatingStore ratings)
{

    public AnalyticsDashboard GetDashboard()
    {
        var dashboard = new AnalyticsDashboard();

        // ─── Sessions ───
        var allSessions = sessions.GetAllSessions();
        dashboard.TotalSessions = allSessions.Count;
        dashboard.TotalMessages = allSessions.Sum(s => s.MessageCount);
        dashboard.AverageSessionMessages = allSessions.Count > 0
            ? Math.Round((double)dashboard.TotalMessages / allSessions.Count, 1)
            : 0;

        // ─── Ratings ───
        var allRatings = ratings.GetAll();
        dashboard.TotalRatings = allRatings.Count;
        dashboard.AverageRating = allRatings.Count > 0
            ? Math.Round(allRatings.Average(r => r.Stars), 2)
            : 0;
        dashboard.RatingDistribution = Enumerable.Range(1, 5)
            .ToDictionary(star => star, star => allRatings.Count(r => r.Stars == star));
        dashboard.RecentRatings = [..ratings.GetRecent(10)];

        // ─── Approvals ───
        var recentApprovals = approvals.GetRecent(200);
        dashboard.TotalApprovals = recentApprovals.Count;
        dashboard.ApprovedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Approved);
        dashboard.RejectedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Rejected);
        dashboard.ExpiredCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Expired);
        dashboard.PendingCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Pending);

        // ─── Escalations ───
        var recentEscalations = escalations.GetRecent(200);
        dashboard.TotalEscalations = recentEscalations.Count;
        dashboard.OpenEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Open);
        dashboard.AcknowledgedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Acknowledged);
        dashboard.ResolvedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Resolved);
        dashboard.DismissedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Dismissed);

        // ─── Intent dağılımı ───
        var intentCounts = new Dictionary<string, int>();
        foreach (var s in allSessions)
        {
            var session = sessions.GetSession(s.SessionId);
            if (session?.State.CurrentIntent is { } intent && !string.IsNullOrEmpty(intent))
            {
                intentCounts[intent] = intentCounts.GetValueOrDefault(intent) + 1;
            }
        }
        dashboard.IntentDistribution = intentCounts;

        // ─── Phase dağılımı ───
        var phaseCounts = new Dictionary<string, int>();
        foreach (var s in allSessions)
        {
            var session = sessions.GetSession(s.SessionId);
            if (session?.State.Phase is { } phase && !string.IsNullOrEmpty(phase))
            {
                phaseCounts[phase] = phaseCounts.GetValueOrDefault(phase) + 1;
            }
        }
        dashboard.PhaseDistribution = phaseCounts;

        // ─── Sentiment dağılımı ───
        var sentimentCounts = new Dictionary<string, int>
        {
            [WellKnown.Sentiments.Positive] = 0,
            [WellKnown.Sentiments.Neutral] = 0,
            [WellKnown.Sentiments.Negative] = 0,
            [WellKnown.Sentiments.Angry] = 0
        };
        var sentimentScores = new List<double>();
        int negativeCount = 0;
        int alertCount = 0;

        foreach (var s in allSessions)
        {
            var session = sessions.GetSession(s.SessionId);
            if (session == null) continue;

            var state = session.State;
            var label = state.Sentiment ?? WellKnown.Sentiments.Neutral;
            if (sentimentCounts.TryGetValue(label, out var count))
                sentimentCounts[label] = count + 1;
            else
                sentimentCounts[label] = 1;

            sentimentScores.Add(state.SentimentScore);

            if (state.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
                negativeCount++;

            if (state.ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative)
                alertCount++;
        }

        dashboard.SentimentDistribution = sentimentCounts;
        dashboard.AverageSentimentScore = sentimentScores.Count > 0
            ? Math.Round(sentimentScores.Average(), 2)
            : 0.5;
        dashboard.NegativeSessionCount = negativeCount;
        dashboard.SentimentAlertCount = alertCount;

        return dashboard;
    }

    /// <summary>
    /// Tek bir oturum için detaylı analytics döner.
    /// Duygu timeline'ı, onay/eskalasyon geçmişi, rating ve durum bilgisi içerir.
    /// </summary>
    public SessionAnalytics? GetSessionAnalytics(string sessionId)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return null;

        var state = session.State;
        var history = sessions.GetHistory(sessionId);

        var result = new SessionAnalytics
        {
            SessionId = sessionId,
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity,
            MessageCount = history.Count,
            TurnCount = state.TurnCount,

            // ─── Durum ───
            CurrentIntent = state.CurrentIntent,
            Phase = state.Phase,
            CustomerId = state.CustomerId,

            // ─── Duygu analizi ───
            Sentiment = state.Sentiment,
            SentimentScore = state.SentimentScore,
            ConsecutiveNegativeTurns = state.ConsecutiveNegativeTurns,
            SentimentTimeline = state.SentimentHistory
                .Select(e => new SentimentTimelineEntry
                {
                    Turn = e.Turn,
                    Label = e.Label,
                    Score = e.Score,
                    Timestamp = e.Timestamp
                })
                .ToList()
        };

        // ─── Rating ───
        var rating = ratings.GetBySession(sessionId);
        if (rating != null)
        {
            result.Rating = new SessionRatingInfo
            {
                Stars = rating.Stars,
                Feedback = rating.Feedback,
                RatedAt = rating.RatedAt
            };
        }

        // ─── Bu session'a ait onaylar ───
        var sessionApprovals = approvals.GetRecent(200)
            .Where(a => a.SessionId == sessionId)
            .ToList();
        result.TotalApprovals = sessionApprovals.Count;
        result.ApprovedCount = sessionApprovals.Count(a => a.Status == ApprovalStatus.Approved);
        result.RejectedCount = sessionApprovals.Count(a => a.Status == ApprovalStatus.Rejected);
        result.ExpiredCount = sessionApprovals.Count(a => a.Status == ApprovalStatus.Expired);
        result.ApprovalDetails = sessionApprovals.Select(a => new ApprovalSummary
        {
            Id = a.Id,
            ToolName = a.ToolName,
            Status = a.Status.ToString().ToLowerInvariant(),
            RequestedAt = a.RequestedAt,
            DecidedAt = a.DecidedAt,
            DecidedBy = a.DecidedBy
        }).ToList();

        // ─── Bu session'a ait eskalasyonlar ───
        var sessionEscalations = escalations.GetRecent(200)
            .Where(e => e.SessionId == sessionId)
            .ToList();
        result.TotalEscalations = sessionEscalations.Count;
        result.OpenEscalations = sessionEscalations.Count(e => e.Status == EscalationStatus.Open);
        result.ResolvedEscalations = sessionEscalations.Count(e => e.Status == EscalationStatus.Resolved);
        result.EscalationDetails = sessionEscalations.Select(e => new EscalationSummary
        {
            Id = e.Id,
            AgentName = e.AgentName,
            Reason = e.Reason,
            Status = e.Status.ToString().ToLowerInvariant(),
            CreatedAt = e.CreatedAt,
            Resolution = e.Resolution
        }).ToList();

        // ─── Toplanan bilgiler ───
        result.CollectedInfo = state.CollectedInfo;

        return result;
    }
}

// ─── Session Analytics DTO'ları ───

public class SessionAnalytics
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public int MessageCount { get; set; }
    public int TurnCount { get; set; }

    // Durum
    public string? CurrentIntent { get; set; }
    public string? Phase { get; set; }
    public string? CustomerId { get; set; }

    // Duygu analizi
    public string? Sentiment { get; set; }
    public double SentimentScore { get; set; }
    public int ConsecutiveNegativeTurns { get; set; }
    public List<SentimentTimelineEntry> SentimentTimeline { get; set; } = new();

    // Rating
    public SessionRatingInfo? Rating { get; set; }

    // Approvals
    public int TotalApprovals { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public List<ApprovalSummary> ApprovalDetails { get; set; } = new();

    // Escalations
    public int TotalEscalations { get; set; }
    public int OpenEscalations { get; set; }
    public int ResolvedEscalations { get; set; }
    public List<EscalationSummary> EscalationDetails { get; set; } = new();

    // Toplanan bilgiler
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
