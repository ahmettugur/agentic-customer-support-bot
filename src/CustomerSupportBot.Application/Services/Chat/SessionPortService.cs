// Application/Services/SessionPortService.cs
// DRIVING PORT IMPL — ISessionPort → Driven portları orkestrasyonla kullanır.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Chat;

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

    public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreateAsync(sessionId, ct);
        _logger.LogDebug("Session retrieved/created: {SessionId}", session.SessionId);
        return session;
    }

    public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        return _sessions.GetAsync(sessionId, ct);
    }

    public async Task UpdateSessionAsync(AgentSession session, CancellationToken ct = default)
    {
        await _sessions.UpdateAsync(session, ct);
        _logger.LogDebug("Session updated: {SessionId}", session.SessionId);
    }

    public async Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(
        string? forCustomerId = null, CancellationToken ct = default)
    {
        return await _sessions.GetAllSessionsAsync(forCustomerId, ct);
    }

    public Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        return _sessions.GetHistoryAsync(sessionId, ct);
    }

    public async Task AddExchangeAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
    {
        // Genel amaçlı inbound API — bir reasoning turuna bağlı değil, dolayısıyla LLM
        // sinyali yok; state çıkarımı kural tabanlı yolla yapılır.
        await _sessions.AddExchangeAsync(sessionId, userMessage, botResponse, signals: null, ct);
        _logger.LogDebug("Exchange added to session: {SessionId}", sessionId);
    }

    public Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
    {
        return _sessions.ExtractAndUpdateStateAsync(sessionId, userMessage, botResponse, ct);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        await _sessions.MutateStateAsync(sessionId, mutator, ct);
    }
}
