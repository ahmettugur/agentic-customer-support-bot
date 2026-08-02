namespace CustomerSupportBot.Web.Models;

// ─── Approvals ────────────────────────────────────────────────────────────────

public sealed record ApprovalRequest(
    string Id,
    string ToolName,
    string? AgentName,
    string? UserQuery,
    string? SessionId,
    // Tool'un ÇAĞRILACAĞI argümanlar (ör. {orderId, reason}). Admin'in kararı verirken
    // gördüğü en kritik veri — hangi siparişin iptal edildiğini bu alan söyler.
    object? Parameters,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? DecisionReason,
    // Sunucu tarafında WellKnown.HighRiskTools'tan türetilir (ApprovalRequest.ReasonRequired) —
    // panel yüksek riskli tool listesinin kendi kopyasını tutmaz, senkron kayması olamaz.
    bool ReasonRequired = false,
    // Bu tool'un neden çağrıldığı — PlanningAgent'ın routing rationale'ı.
    string? Justification = null,
    // Reasoning trace'e derin link için (TraceDetailPanel / Replay).
    string? TraceId = null,
    // Otomatik red süresi; panelde sabit "60 saniye" yazmak yerine sunucudan okunur.
    int TimeoutSeconds = 60
);

// ─── Escalations ─────────────────────────────────────────────────────────────

public sealed record EscalationRequest(
    string Id,
    string? AgentName,
    string? AssignedTo,
    string? Reason,
    string? UserQuery,
    string? SessionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution,
    string[]? MissingContext,
    string? ResponseSummary
);

// ─── Chat Sessions ────────────────────────────────────────────────────────────

public sealed record ActiveChatSession(
    string SessionId,
    string? HumanAgent,
    int MessageCount,
    string? SentimentLabel,
    double? SentimentScore,
    DateTimeOffset EnteredHumanModeAt
);

public sealed record ChatHistoryMessage(
    string Sender,
    string Text,
    DateTimeOffset Timestamp
);

// ─── Analytics ───────────────────────────────────────────────────────────────

public sealed record AnalyticsDashboard(
    int TotalSessions,
    int TotalMessages,
    double AverageRating,
    int TotalRatings,
    double AverageMessagesPerSession,
    double AverageSentimentScore,
    int NegativeSessions,
    int SentimentAlerts,
    Dictionary<string, int>? RatingDistribution,
    Dictionary<string, int>? SentimentDistribution,
    Dictionary<string, int>? IntentDistribution,
    Dictionary<string, int>? PhaseDistribution,
    ApprovalStats? ApprovalStats,
    EscalationStats? EscalationStats,
    RecentRating[]? RecentRatings
);

public sealed record ApprovalStats(int Total, int Approved, int Rejected, int TimedOut);
public sealed record EscalationStats(int Total, int Resolved, int Dismissed);
public sealed record RecentRating(string SessionId, int Stars, string? Feedback, DateTimeOffset RatedAt);

public sealed record SessionSummary(string SessionId, DateTimeOffset LastActivity, int MessageCount, string? Title = null);

// ─── Session Analytics ──────────────────────────────────────────────────────

public sealed record SessionAnalyticsModel(
    string SessionId,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    int TurnCount,
    string? CurrentIntent,
    string? Phase,
    string? CustomerId,
    string? Sentiment,
    double SentimentScore,
    int ConsecutiveNegativeTurns,
    List<SentimentTimelineItem> SentimentTimeline,
    SessionRatingItem? Rating,
    int TotalApprovals,
    int ApprovedCount,
    int RejectedCount,
    int ExpiredCount,
    List<ApprovalSummaryItem> ApprovalDetails,
    int TotalEscalations,
    int OpenEscalations,
    int ResolvedEscalations,
    List<EscalationSummaryItem> EscalationDetails,
    Dictionary<string, string> CollectedInfo
);

public sealed record SentimentTimelineItem(int Turn, string Label, double Score, DateTime Timestamp);
public sealed record SessionRatingItem(int Stars, string? Feedback, DateTime RatedAt);
public sealed record ApprovalSummaryItem(string Id, string ToolName, string Status, DateTime RequestedAt, DateTime? DecidedAt, string? DecidedBy);
public sealed record EscalationSummaryItem(string Id, string? AgentName, string Reason, string Status, DateTime CreatedAt, string? Resolution);

// ─── Agents ──────────────────────────────────────────────────────────────────

public sealed record AgentInfo(string Id, string DisplayName, bool IsActive);

// ─── SLA ─────────────────────────────────────────────────────────────────────

public sealed record SlaStatus(
    bool Enabled,
    int PollIntervalSeconds,
    SlaApprovalStats? Approvals,
    SlaEscalationStats? Escalations
);

public sealed record SlaApprovalStats(
    int PendingCount,
    double? OldestSeconds,
    double WarnAfter,
    double BreachAfter,
    string? OnBreach,
    int BreachCountRecent
);

public sealed record SlaEscalationStats(
    int OpenCount,
    double? OldestSeconds,
    double WarnAfter,
    double BreachAfter,
    bool BoostPriorityOnBreach,
    int BreachCountRecent
);

public sealed record SlaEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string Severity,
    string TargetId,
    string? Action,
    double? AgeSeconds,
    string? Note
);

public sealed record SlaEventsResponse(int TotalCount, List<SlaEvent> Items);

// ─── Improvements ──────────────────────────────────────────────────────────────

public sealed record LessonProposal(
    string Id,
    string? Title,
    string? LessonText,
    string? Observation,
    string? SuggestedAgent,
    string[]? SourceTraceIds,
    string Status,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    // Onaylanan ders vektör hafızaya yazıldıysa dolu olur. Approved olduğu hâlde BOŞ ise
    // ders yalnızca DB'de durur ve konuşmalara hiç context olarak girmez — yani onay
    // pratikte etkisizdir (LessonMiner.ApproveAsync vektör yazım hatasını yutar).
    string? VectorMemoryId = null
);
