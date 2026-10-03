using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public sealed record ChatSessionSentimentSnapshot(
    string Sentiment,
    double Score,
    int ConsecutiveNegative,
    IReadOnlyList<SentimentEntry> History);

public sealed record ChatSessionTakeoverResult(
    string SessionId,
    string HumanAgent,
    int EscalationsAcknowledged,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;
}

public sealed record ChatSessionReleaseResult(
    string SessionId,
    int EscalationsResolved,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;
}

public sealed record ChatSessionMessageResult(
    string SessionId,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;
}

public sealed record ChatSessionReplanResult(
    string SessionId,
    string? EscalationId,
    string RequestedBy,
    int EscalationsResolved,
    bool ReleasedFromHuman,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;
}

/// <summary>
/// Admin/agent panelinin canlı takeover, history ve replan akışları için
/// kullandığı primary port.
/// </summary>
public interface IChatSessionPort
{
    IReadOnlyList<ChatSessionState> GetActive();
    ChatSessionState GetStateOrDefault(string sessionId);
    Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50);
    Task<ChatSessionSentimentSnapshot?> GetSentimentAsync(string sessionId, CancellationToken ct = default);
    Task PublishSystemMessageAsync(string sessionId, string text);
    Task<ChatSessionTakeoverResult> TakeOverAsync(string sessionId, string humanAgent, string? agentId = null);
    Task<ChatSessionReleaseResult> ReleaseAsync(string sessionId, string? agentId = null);
    Task<ChatSessionMessageResult> SendAdminMessageAsync(string sessionId, string humanAgent, string text, CancellationToken ct = default);
    Task<ChatSessionReplanResult> ReplanSessionAsync(string sessionId, string requestedBy, string? note, CancellationToken ct = default);
    Task<ChatSessionReplanResult> ReplanEscalationAsync(string escalationId, string requestedBy, string? note, CancellationToken ct = default);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct);
    IReadOnlyList<EscalationRequest> GetOpenEscalations();
    Task<int> DismissOrphanedEscalationsAsync(string sessionId);
}
