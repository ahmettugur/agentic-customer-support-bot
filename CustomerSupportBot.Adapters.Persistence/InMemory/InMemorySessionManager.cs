// Services/InMemorySessionManager.cs
// ISessionManager'ın bellek içi implementasyonu.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

/// <summary>
/// Bellek içi oturum yöneticisi.
/// Hem mesaj geçmişi hem de oturum durumu (state) yönetir.
/// Thread-safe erişim için ConcurrentDictionary kullanılır.
/// </summary>
public class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ConversationMessage>> _messageHistory = new();
    private readonly IAppDistributedLock _distributedLock;

    public InMemorySessionManager(IAppDistributedLock distributedLock)
    {
        _distributedLock = distributedLock;
    }

    // ─── ISessionManager (eski ISessionManager) ───

    public AgentSession GetOrCreate(string? sessionId)
    {
        sessionId ??= Guid.NewGuid().ToString();

        return _sessions.GetOrAdd(sessionId, id => new AgentSession
        {
            SessionId = id,
            CreatedAt = DateTime.Now,
            LastActivity = DateTime.Now,
            State = new SessionState()
        });
    }

    public AgentSession? Get(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public void Update(AgentSession session)
    {
        session.LastActivity = DateTime.Now;
        _sessions[session.SessionId] = session;
    }

    public IReadOnlyList<AgentSession> GetAll()
    {
        return _sessions.Values.ToList();
    }

    public void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse)
    {
        var session = Get(sessionId);
        if (session == null) return;

        // Lock gerekmez — aynı session için aynı anda tek bot pipeline çalışır.
        // ConcurrentDictionary bireysel okuma/yazma için thread-safe'dir.
        // Non-atomic read-modify-write işlemleri MutateStateAsync üzerinden yapılmalıdır.
        ExtractAndUpdateStateCore(session, userMessage, botResponse);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        var session = Get(sessionId);
        if (session == null) return;

        await using var handle = await _distributedLock
            .AcquireAsync($"session:{sessionId}", ct: ct)
            .ConfigureAwait(false);

        mutator(session.State);
        Update(session);
    }

    private void ExtractAndUpdateStateCore(
        AgentSession session, string userMessage, string botResponse,
        IReadOnlyList<ConversationMessage>? priorHistory = null)
    {
        SessionStateExtractor.ExtractAndApply(session.State, userMessage, botResponse, priorHistory);
        Update(session);
    }

    // ─── Konuşma geçmişi ───

    public List<ConversationMessage> GetHistory(string sessionId)
    {
        if (_messageHistory.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return new List<ConversationMessage>(history);
            }
        }
        return new List<ConversationMessage>();
    }

    public void AddExchange(string sessionId, string userQuery, string assistantResponse)
    {
        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        List<ConversationMessage> priorHistorySnapshot;
        lock (history)
        {
            // Bu turdan ÖNCEKİ geçmiş — SessionStateExtractor'ın bağlam takibi için
            // (eklemeden önce alınmalı, yoksa "önceki tur" bu turun kendisi olur).
            priorHistorySnapshot = new List<ConversationMessage>(history);
            history.Add(new ConversationMessage(ConversationRoles.User, userQuery));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, assistantResponse));
        }

        // Oturumun var olduğundan emin ol
        var session = GetOrCreate(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        // State çıkarma
        ExtractAndUpdateStateCore(session, userQuery, assistantResponse, priorHistorySnapshot);
    }

    public void ClearSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _messageHistory.TryRemove(sessionId, out _);
    }

    public void AppendAssistantMessage(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (history)
        {
            // Bot Human modda placeholder olarak "" assistant mesajı ekliyor.
            // Admin yanıtı geldiğinde bunu doldur; aksi halde yeni mesaj ekle.
            if (history.Count > 0
                && history[^1].Role == ConversationRoles.Assistant
                && string.IsNullOrEmpty(history[^1].Text))
            {
                history[^1] = new ConversationMessage(ConversationRoles.Assistant, text);
            }
            else
            {
                history.Add(new ConversationMessage(ConversationRoles.Assistant, text));
            }
        }

        var session = GetOrCreate(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;
    }

    public void AppendUserMessage(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (history)
        {
            history.Add(new ConversationMessage(ConversationRoles.User, text));
        }

        var session = GetOrCreate(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;
    }

    public List<SessionInfo> GetAllSessions()
    {
        var result = new List<SessionInfo>();

        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;
            var history = GetHistory(kvp.Key);
            var firstUserMsg = history.FirstOrDefault(m => m.Role == ConversationRoles.User)?.Text;

            result.Add(new SessionInfo
            {
                SessionId = session.SessionId,
                Title = firstUserMsg != null
                    ? (firstUserMsg.Length > 50 ? firstUserMsg[..50] + "..." : firstUserMsg)
                    : WellKnown.FallbackMessages.NewChat,
                LastActivity = session.LastActivity,
                MessageCount = history.Count
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }

}

