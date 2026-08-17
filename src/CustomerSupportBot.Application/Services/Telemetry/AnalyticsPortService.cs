// Application/Services/AnalyticsPortService.cs
// DRIVING PORT IMPL — IAnalyticsPort → Analitik veri orkestrasyonu.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Telemetry;

/// <summary>
/// Analitik verileri driving port implementasyonu.
/// AnalyticsEndpoints bu sınıfı IAnalyticsPort olarak kullanır.
/// </summary>
public sealed class AnalyticsPortService : IAnalyticsPort
{
    private readonly IRatingStore _ratings;
    private readonly ISessionManager _sessions;
    private readonly IApprovalQueue _approvals;
    private readonly IEscalationSink _escalations;
    private readonly ILogger<AnalyticsPortService> _logger;

    public AnalyticsPortService(
        IRatingStore ratings,
        ISessionManager sessions,
        IApprovalQueue approvals,
        IEscalationSink escalations,
        ILogger<AnalyticsPortService> logger)
    {
        _ratings = ratings;
        _sessions = sessions;
        _approvals = approvals;
        _escalations = escalations;
        _logger = logger;
    }

    public ConversationRating Rate(string sessionId, int stars, string? comment = null)
    {
        if (stars < 1 || stars > 5)
            throw new ArgumentOutOfRangeException(nameof(stars), "Rating must be between 1 and 5");

        var rating = _ratings.Submit(sessionId, stars, comment);
        _logger.LogInformation(
            "Rating submitted: session={SessionId} stars={Stars}",
            sessionId, stars);
        return rating;
    }

    public ConversationRating? GetRating(string sessionId)
    {
        return _ratings.GetBySession(sessionId);
    }

    public IReadOnlyList<ConversationRating> GetRecentRatings(int count = 20)
    {
        return _ratings.GetRecent(count);
    }

    public IReadOnlyList<ConversationRating> GetAllRatings()
    {
        return _ratings.GetAll();
    }

    public async Task<object> GetSummaryAsync(CancellationToken ct = default)
    {
        var allRatings = _ratings.GetAll();
        var sessionInfos = await _sessions.GetAllSessionsAsync(ct);
        var allSessions = await _sessions.GetAllAsync(ct);
        var recentApprovals = _approvals.GetRecent(200);
        var recentEscalations = _escalations.GetRecent(200);

        var summary = new
        {
            TotalSessions = sessionInfos.Count,
            TotalMessages = sessionInfos.Sum(s => s.MessageCount),
            AverageSessionMessages = sessionInfos.Count > 0
                ? Math.Round((double)sessionInfos.Sum(s => s.MessageCount) / sessionInfos.Count, 1)
                : 0,
            TotalRatings = allRatings.Count,
            AverageRating = allRatings.Count > 0
                ? Math.Round(allRatings.Average(r => r.Stars), 2)
                : 0,
            RatingDistribution = Enumerable.Range(1, 5)
                .ToDictionary(star => star, star => allRatings.Count(r => r.Stars == star)),
            TotalApprovals = recentApprovals.Count,
            ApprovedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Approved),
            RejectedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Rejected),
            PendingCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Pending),
            TotalEscalations = recentEscalations.Count,
            OpenEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Open),
            ResolvedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Resolved),
            IntentDistribution = allSessions
                .Where(s => !string.IsNullOrEmpty(s.State?.CurrentIntent))
                .GroupBy(s => s.State!.CurrentIntent!)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return summary;
    }

    public async Task<AnalyticsDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var dashboard = new AnalyticsDashboard();

        var allSessions = await _sessions.GetAllSessionsAsync(ct);
        dashboard.TotalSessions = allSessions.Count;
        dashboard.TotalMessages = allSessions.Sum(s => s.MessageCount);
        dashboard.AverageSessionMessages = allSessions.Count > 0
            ? Math.Round((double)dashboard.TotalMessages / allSessions.Count, 1)
            : 0;

        var allRatings = _ratings.GetAll();
        dashboard.TotalRatings = allRatings.Count;
        dashboard.AverageRating = allRatings.Count > 0
            ? Math.Round(allRatings.Average(r => r.Stars), 2)
            : 0;
        dashboard.RatingDistribution = Enumerable.Range(1, 5)
            .ToDictionary(star => star, star => allRatings.Count(r => r.Stars == star));
        dashboard.RecentRatings = [..allRatings.TakeLast(10)];

        var recentApprovals = _approvals.GetRecent(200);
        dashboard.TotalApprovals = recentApprovals.Count;
        dashboard.ApprovedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Approved);
        dashboard.RejectedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Rejected);
        dashboard.ExpiredCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Expired);
        dashboard.PendingCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Pending);

        var recentEscalations = _escalations.GetRecent(200);
        dashboard.TotalEscalations = recentEscalations.Count;
        dashboard.OpenEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Open);
        dashboard.AcknowledgedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Acknowledged);
        dashboard.ResolvedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Resolved);
        dashboard.DismissedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Dismissed);

        var intentCounts = new Dictionary<string, int>();
        var phaseCounts = new Dictionary<string, int>();
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
            var session = await _sessions.GetAsync(s.SessionId, ct);
            if (session == null) continue;
            var state = session.State;

            if (!string.IsNullOrEmpty(state.CurrentIntent))
                intentCounts[state.CurrentIntent] = intentCounts.GetValueOrDefault(state.CurrentIntent) + 1;

            if (!string.IsNullOrEmpty(state.Phase))
                phaseCounts[state.Phase] = phaseCounts.GetValueOrDefault(state.Phase) + 1;

            var label = state.Sentiment ?? WellKnown.Sentiments.Neutral;
            sentimentCounts[label] = sentimentCounts.GetValueOrDefault(label) + 1;
            sentimentScores.Add(state.SentimentScore);

            if (state.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
                negativeCount++;
            if (state.ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative)
                alertCount++;
        }

        dashboard.IntentDistribution = intentCounts;
        dashboard.PhaseDistribution = phaseCounts;
        dashboard.SentimentDistribution = sentimentCounts;
        dashboard.AverageSentimentScore = sentimentScores.Count > 0
            ? Math.Round(sentimentScores.Average(), 2)
            : 0.5;
        dashboard.NegativeSessionCount = negativeCount;
        dashboard.SentimentAlertCount = alertCount;

        return dashboard;
    }

    public async Task<SessionAnalytics?> GetSessionAnalyticsAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct);
        if (session == null) return null;

        var state = session.State;
        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        var result = new SessionAnalytics
        {
            SessionId = sessionId,
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity,
            MessageCount = history.Count,
            TurnCount = state.TurnCount,
            CurrentIntent = state.CurrentIntent,
            Phase = state.Phase,
            CustomerId = state.CustomerId,
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

        var rating = _ratings.GetBySession(sessionId);
        if (rating != null)
        {
            result.Rating = new SessionRatingInfo
            {
                Stars = rating.Stars,
                Feedback = rating.Feedback,
                RatedAt = rating.RatedAt
            };
        }

        var sessionApprovals = _approvals.GetRecent(200)
            .Where(a => a.SessionId == sessionId).ToList();
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

        var sessionEscalations = _escalations.GetRecent(200)
            .Where(e => e.SessionId == sessionId).ToList();
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

        result.CollectedInfo = state.CollectedInfo;
        return result;
    }
}
