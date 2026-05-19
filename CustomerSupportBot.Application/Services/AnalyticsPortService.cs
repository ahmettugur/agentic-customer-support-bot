// Application/Services/AnalyticsPortService.cs
// DRIVING PORT IMPL — IAnalyticsPort → Analitik veri orkestrasyonu.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Analitik verileri driving port implementasyonu.
/// AnalyticsEndpoints bu sınıfı IAnalyticsPort olarak kullanır.
/// </summary>
public sealed class AnalyticsPortService : IAnalyticsPort
{
    private readonly IRatingRepository _ratings;
    private readonly ISessionRepository _sessions;
    private readonly IApprovalQueueRepository _approvals;
    private readonly IEscalationRepository _escalations;
    private readonly ILogger<AnalyticsPortService> _logger;

    public AnalyticsPortService(
        IRatingRepository ratings,
        ISessionRepository sessions,
        IApprovalQueueRepository approvals,
        IEscalationRepository escalations,
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

    public IReadOnlyList<ConversationRating> GetAllRatings()
    {
        return _ratings.GetAll();
    }

    public object GetSummary()
    {
        var allRatings = _ratings.GetAll();
        var sessionInfos = _sessions.GetAllSessions();
        var allSessions = _sessions.GetAll();
        var recentApprovals = _approvals.GetRecent(200);
        var recentEscalations = _escalations.GetRecent(200);

        var summary = new
        {
            // Sessions
            TotalSessions = sessionInfos.Count,
            TotalMessages = sessionInfos.Sum(s => s.MessageCount),
            AverageSessionMessages = sessionInfos.Count > 0
                ? Math.Round((double)sessionInfos.Sum(s => s.MessageCount) / sessionInfos.Count, 1)
                : 0,

            // Ratings
            TotalRatings = allRatings.Count,
            AverageRating = allRatings.Count > 0
                ? Math.Round(allRatings.Average(r => r.Stars), 2)
                : 0,
            RatingDistribution = Enumerable.Range(1, 5)
                .ToDictionary(star => star, star => allRatings.Count(r => r.Stars == star)),

            // Approvals
            TotalApprovals = recentApprovals.Count,
            ApprovedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Approved),
            RejectedCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Rejected),
            PendingCount = recentApprovals.Count(a => a.Status == ApprovalStatus.Pending),

            // Escalations
            TotalEscalations = recentEscalations.Count,
            OpenEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Open),
            ResolvedEscalations = recentEscalations.Count(e => e.Status == EscalationStatus.Resolved),

            // Intent distribution
            IntentDistribution = allSessions
                .Where(s => !string.IsNullOrEmpty(s.State?.CurrentIntent))
                .GroupBy(s => s.State!.CurrentIntent!)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return summary;
    }
}
