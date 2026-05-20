// Application/Services/SessionPortService.cs
// DRIVING PORT IMPL — ISessionPort → Driven portları orkestrasyonla kullanır.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Oturum yönetimi driving port implementasyonu.
/// HTTP adaptörü (SessionEndpoints) bu sınıfı ISessionPort olarak kullanır.
/// </summary>
public sealed class SessionPortService : ISessionPort
{
    private readonly ISessionManager _sessions;
    private readonly ILogger<SessionPortService> _logger;

    public SessionPortService(
        ISessionManager sessions,
        ILogger<SessionPortService> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    public AgentSession GetOrCreateSession(string? sessionId)
    {
        var session = _sessions.GetOrCreate(sessionId);
        _logger.LogDebug("Session retrieved/created: {SessionId}", session.SessionId);
        return session;
    }

    public AgentSession? GetSession(string sessionId)
    {
        return _sessions.Get(sessionId);
    }

    public void UpdateSession(AgentSession session)
    {
        _sessions.Update(session);
        _logger.LogDebug("Session updated: {SessionId}", session.SessionId);
    }

    public IReadOnlyList<SessionInfo> GetAllSessions()
    {
        return _sessions.GetAllSessions();
    }

    public List<ConversationMessage> GetHistory(string sessionId)
    {
        return _sessions.GetHistory(sessionId);
    }

    public void AddExchange(string sessionId, string userMessage, string botResponse)
    {
        _sessions.AddExchange(sessionId, userMessage, botResponse);
        _logger.LogDebug("Exchange added to session: {SessionId}", sessionId);
    }

    public void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse)
    {
        _sessions.ExtractAndUpdateState(sessionId, userMessage, botResponse);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        await _sessions.MutateStateAsync(sessionId, mutator, ct);
    }
}
