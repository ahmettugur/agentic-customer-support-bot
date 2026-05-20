// Ports/Driving/IChatSessionPort.cs
// PRIMARY PORT — canlı takeover, sohbet geçmişi ve replan orkestrasyonu.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

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
    IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50);
    ChatSessionSentimentSnapshot? GetSentiment(string sessionId);
    void PublishSystemMessage(string sessionId, string text);
    ChatSessionTakeoverResult TakeOver(string sessionId, string humanAgent, string? agentId = null);
    ChatSessionReleaseResult Release(string sessionId, string? agentId = null);
    ChatSessionMessageResult SendAdminMessage(string sessionId, string humanAgent, string text);
    ChatSessionReplanResult ReplanSession(string sessionId, string requestedBy, string? note);
    ChatSessionReplanResult ReplanEscalation(string escalationId, string requestedBy, string? note);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct);
    IReadOnlyList<EscalationRequest> GetOpenEscalations();
    int DismissOrphanedEscalations(string sessionId);
}
