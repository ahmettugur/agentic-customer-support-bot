// Services/InMemorySessionManager.cs
// ISessionManager'ın bellek içi implementasyonu. Gerçek I/O yok, bu yüzden async
// metodlar Task.FromResult/Task.CompletedTask ile senkron tamamlanır — arayüz
// PostgresSessionManager ile ortak olduğu için async imzalar korunur.

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

    // ─── ISessionManager ───

    // Tek süreç: cache zaten tek kaynaktır, tazelenecek ayrı bir depo yoktur.
    public Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct = default) =>
        GetOrCreateAsync(sessionId, ct);

    public Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString();

        var session = _sessions.GetOrAdd(sessionId, id => new AgentSession
        {
            SessionId = id,
            CreatedAt = DateTime.Now,
            LastActivity = DateTime.Now,
            State = new SessionState()
        });
        return Task.FromResult(session);
    }

    public Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        return Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);
    }

    public Task UpdateAsync(AgentSession session, CancellationToken ct = default)
    {
        session.LastActivity = DateTime.Now;
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AgentSession>>(_sessions.Values.ToList());
    }

    public async Task ExtractAndUpdateStateAsync(
        string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null) return;

        // Lock gerekmez — aynı session için aynı anda tek bot pipeline çalışır.
        // ConcurrentDictionary bireysel okuma/yazma için thread-safe'dir.
        // Non-atomic read-modify-write işlemleri MutateStateAsync üzerinden yapılmalıdır.
        await ExtractAndUpdateStateCoreAsync(session, userMessage, botResponse, null, ct).ConfigureAwait(false);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null) return;

        await using var handle = await _distributedLock
            .AcquireAsync($"session:{sessionId}", ct: ct)
            .ConfigureAwait(false);

        mutator(session.State);
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    private async Task ExtractAndUpdateStateCoreAsync(
        AgentSession session, string userMessage, string botResponse,
        IReadOnlyList<ConversationMessage>? priorHistory, CancellationToken ct,
        TurnSignals? signals = null)
    {
        // Kilit gerekçesi için bkz. PostgresSessionManager.ExtractAndUpdateStateCoreAsync.
        lock (session)
        {
            SessionStateExtractor.ExtractAndApply(session.State, userMessage, botResponse, priorHistory, signals);
        }
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    // ─── Konuşma geçmişi ───

    public Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        if (_messageHistory.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return Task.FromResult(new List<ConversationMessage>(history));
            }
        }
        return Task.FromResult(new List<ConversationMessage>());
    }

    public async Task AddExchangeAsync(
        string sessionId, string userQuery, string assistantResponse,
        TurnSignals? signals = null, CancellationToken ct = default)
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
        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        // State çıkarma
        await ExtractAndUpdateStateCoreAsync(session, userQuery, assistantResponse, priorHistorySnapshot, ct, signals)
            .ConfigureAwait(false);
    }

    public Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        _messageHistory.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    public async Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct = default)
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

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;
    }

    public async Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (history)
        {
            history.Add(new ConversationMessage(ConversationRoles.User, text));
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;
    }

    public async Task<List<SessionInfo>> GetAllSessionsAsync(
        string? forCustomerId = null, CancellationToken ct = default)
    {
        var result = new List<SessionInfo>();

        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;

            if (forCustomerId is not null &&
                !string.Equals(session.State.AuthenticatedCustomerId, forCustomerId, StringComparison.Ordinal))
                continue;

            var history = await GetHistoryAsync(kvp.Key, ct).ConfigureAwait(false);
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
