using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Oturum yaşam döngüsü ve konuşma geçmişi için primary port.
/// </summary>
public interface ISessionPort
{
    AgentSession GetOrCreateSession(string? sessionId);
    AgentSession? GetSession(string sessionId);
    void UpdateSession(AgentSession session);
    IReadOnlyList<SessionInfo> GetAllSessions();
    List<ConversationMessage> GetHistory(string sessionId);
    void AddExchange(string sessionId, string userMessage, string botResponse);
    void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse);
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);
}
