using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Oturum yaşam döngüsü ve konuşma geçmişi için primary port.
/// </summary>
public interface ISessionPort
{
    Task<AgentSession> GetOrCreateSessionAsync(string? sessionId, CancellationToken ct = default);
    Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task UpdateSessionAsync(AgentSession session, CancellationToken ct = default);
    Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken ct = default);
    Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default);
    Task AddExchangeAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default);
    Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default);
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);
}
