// Ports/Driven/Persistence/ISessionManager.cs
// SECONDARY PORT — Oturum kalıcılığı + konuşma geçmişi.
// Postgres adaptörü: PostgresSessionManager
// InMemory adaptörü: InMemorySessionManager

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Oturum bilgisi özeti (sidebar listesi için).
/// </summary>
public class SessionInfo
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime LastActivity { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// Oturum ve konuşma geçmişi kalıcılığı için secondary (driven) port.
/// Core bu port'a bağımlıdır; hangi adaptörün (Postgres/InMemory) kullanıldığını bilmez.
/// </summary>
public interface ISessionManager
{
    // ─── Session yönetimi ───

    AgentSession GetOrCreate(string? sessionId);
    AgentSession? Get(string sessionId);
    void Update(AgentSession session);
    IReadOnlyList<AgentSession> GetAll();
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);

    // ─── Konuşma geçmişi ───

    List<ConversationMessage> GetHistory(string sessionId);
    void AddExchange(string sessionId, string userMessage, string botResponse);
    void AppendAssistantMessage(string sessionId, string text);
    void ClearSession(string sessionId);
    List<SessionInfo> GetAllSessions();

    // ─── State extraction ───

    void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse);
}
