// Ports/Driving/ISessionPort.cs
// PRIMARY PORT — Oturum yönetimi (SessionEndpoints, AdminEndpoints tarafından kullanılır).

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Oturum yaşam döngüsü ve konuşma geçmişi için primary port.
/// </summary>
public interface ISessionPort
{
    AgentSession GetOrCreateSession(string? sessionId);
    AgentSession? GetSession(string sessionId);
    void UpdateSession(AgentSession session);
    List<ChatMessage> GetHistory(string sessionId);
    void AddExchange(string sessionId, string userMessage, string botResponse);
    void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse);
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);
}
