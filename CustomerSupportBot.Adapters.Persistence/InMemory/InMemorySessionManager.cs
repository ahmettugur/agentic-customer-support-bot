// Services/InMemorySessionManager.cs
// ISessionManager'ın bellek içi implementasyonu.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

/// <summary>
/// Bellek içi oturum yöneticisi.
/// Hem mesaj geçmişi hem de oturum durumu (state) yönetir.
/// Thread-safe erişim için ConcurrentDictionary kullanılır.
/// </summary>
public partial class InMemorySessionManager : ISessionManager
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

    private void ExtractAndUpdateStateCore(AgentSession session, string userMessage, string botResponse)
    {
        var state = session.State;
        state.TurnCount++;

        // Müşteri kimlik numarası çıkarma (CUST-XXX formatı)
        var custMatch = CustomerIdPattern().Match(userMessage);
        if (custMatch.Success)
        {
            state.CustomerId = custMatch.Value;
        }
        else
        {
            // Bot yanıtından da çıkar (ajan müşteriye kimliğini söyleyebilir)
            custMatch = CustomerIdPattern().Match(botResponse);
            if (custMatch.Success && state.CustomerId == null)
            {
                state.CustomerId = custMatch.Value;
            }
        }

        // Sipariş numarası çıkarma (ORD-XXX formatı)
        var orderMatch = OrderIdPattern().Match(userMessage);
        if (orderMatch.Success)
        {
            state.CollectedInfo["LastMentionedOrderId"] = orderMatch.Value;
        }

        // Niyet tespiti (basit kural tabanlı)
        state.CurrentIntent = DetectUserIntent(userMessage);

        // Güncelleme
        state.Phase = DetermineConversationPhase(state.TurnCount, botResponse);

        // Duygu analizi (hızlı kural tabanlı — reasoning LLM sonucu ile override edilebilir)
        var (sentimentLabel, sentimentScore) = DetectSentiment(userMessage);
        state.Sentiment = sentimentLabel;
        state.SentimentScore = sentimentScore;

        state.SentimentHistory.Add(new SentimentEntry
        {
            Turn = state.TurnCount,
            Label = sentimentLabel,
            Score = sentimentScore
        });

        // Son 20 tur dışını temizle
        if (state.SentimentHistory.Count > 20)
            state.SentimentHistory.RemoveRange(0, state.SentimentHistory.Count - 20);

        // Ardışık negatif sayacı
        if (sentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
            state.ConsecutiveNegativeTurns++;
        else
            state.ConsecutiveNegativeTurns = 0;

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
        lock (history)
        {
            history.Add(new ConversationMessage(ConversationRoles.User, userQuery));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, assistantResponse));
        }

        // Oturumun var olduğundan emin ol
        var session = GetOrCreate(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        // State çıkarma
        ExtractAndUpdateState(sessionId, userQuery, assistantResponse);
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

    // ─── YARDIMCI METODLAR ───

    /// <summary>
    /// Kullanıcı mesajından niyet algılar.
    /// Önce <see cref="WellKnown.IntentKeywords"/> mapping'i, sonra özel kompozit kural
    /// (sipariş + durum/takip/nerede → OrderInquiry).
    /// </summary>
    private static string DetectUserIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        // Özel kural: "sipariş" kelimesi tek başına yetmez, durum/takip/nerede ile birleşmeli
        if (lower.Contains("sipariş") &&
            (lower.Contains("durum") || lower.Contains("takip") || lower.Contains("nerede")))
        {
            return WellKnown.Intents.OrderInquiry;
        }

        // Tablo tabanlı eşleşme — ilk eşleşen niyet seçilir
        foreach (var (intent, keywords) in WellKnown.IntentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k)))
            {
                return intent;
            }
        }

        return WellKnown.Intents.General;
    }

    private static string DetermineConversationPhase(int turnCount, string botResponse)
    {
        return turnCount switch
        {
            1 => WellKnown.Phases.Inquiry,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.SuccessMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Resolution,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.MissingInfoMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Inquiry,
            _ => WellKnown.Phases.Action
        };
    }

    /// <summary>
    /// Kural tabanlı hızlı duygu analizi. WellKnown.SentimentKeywords tablosunu kullanır.
    /// LLM reasoning sonucu ile daha sonra override edilebilir.
    /// </summary>
    private static (string Label, double Score) DetectSentiment(string message)
    {
        var lower = message.ToLowerInvariant();

        foreach (var (sentiment, score, keywords) in WellKnown.SentimentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k)))
            {
                return (sentiment, score);
            }
        }

        return (WellKnown.Sentiments.Neutral, 0.5);
    }

    [GeneratedRegex(@"CUST-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex CustomerIdPattern();

    [GeneratedRegex(@"ORD-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex OrderIdPattern();
}

