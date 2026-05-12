// Services/Persistence/PostgresSessionManager.cs
// Hibrit cache + PostgreSQL oturum yöneticisi.
// ISessionManager (= IConversationStore + state) implementasyonu.
//
// Davranış (in-memory ile aynı API ve semantik):
//   - Cache: ConcurrentDictionary<sessionId, AgentSession> + message history.
//   - AddExchange: cache + DB (UPSERT session, INSERT 2 message).
//   - AppendAssistantMessage: son boş assistant mesajını UPDATE veya INSERT.
//   - ExtractAndUpdateState: Mevcut in-memory mantık aynen — sonra UPSERT session.
//   - ClearSession: cache + DB DELETE (cascade → messages).
//   - GetAllSessions: cache (her session ilk kullanımda lazy hydrate).
//
// Basitlik için ExtractAndUpdateState içindeki regex/duygu/intent kuralları
// InMemorySessionManager'dan birebir kopyalandı (tek doğruluk kaynağı için
// ortak helper'a refactor Faz 4 cleanup'ında düşünülebilir).

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Chat;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed partial class PostgresSessionManager : ISessionManager
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresSessionManager> _logger;
    private readonly IAppDistributedLock _distributedLock;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _messageHistory = new();
    private readonly ConcurrentDictionary<string, byte> _hydratedSessions = new();
    private readonly object _allHydrationLock = new();
    private volatile bool _allListHydrated;

    public PostgresSessionManager(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IAppDistributedLock distributedLock,
        ILogger<PostgresSessionManager> logger)
    {
        _dbFactory = dbFactory;
        _distributedLock = distributedLock;
        _logger = logger;
    }

    // ─── ISessionManager ───

    public AgentSession GetOrCreateSession(string? sessionId)
    {
        sessionId ??= Guid.NewGuid().ToString();
        EnsureSessionHydrated(sessionId);

        return _sessions.GetOrAdd(sessionId, id =>
        {
            var session = new AgentSession
            {
                SessionId = id,
                CreatedAt = DateTime.Now,
                LastActivity = DateTime.Now,
                State = new SessionState()
            };
            try { UpsertSessionAsync(session).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Session] UPSERT (create) başarısız. Id={Id}", id);
            }
            return session;
        });
    }

    public AgentSession? GetSession(string sessionId)
    {
        EnsureSessionHydrated(sessionId);
        return _sessions.TryGetValue(sessionId, out var s) ? s : null;
    }

    public void UpdateSession(AgentSession session)
    {
        session.LastActivity = DateTime.Now;
        _sessions[session.SessionId] = session;
        try { UpsertSessionAsync(session).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] UPSERT başarısız. Id={Id}", session.SessionId);
        }
    }

    public void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse)
    {
        var session = GetSession(sessionId);
        if (session is null) return;

        // Lock gerekmez — aynı session için aynı anda tek bot pipeline çalışır
        // (ConcurrentDictionary + in-memory cache). Sync-over-async lock pattern
        // thread pool starvation'a neden oluyordu.
        ExtractAndUpdateStateCore(session, userMessage, botResponse);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        var session = GetSession(sessionId);
        if (session is null) return;

        await using var handle = await _distributedLock
            .AcquireAsync($"session:{sessionId}", ct: ct)
            .ConfigureAwait(false);

        mutator(session.State);
        UpdateSession(session);
    }

    private void ExtractAndUpdateStateCore(AgentSession session, string userMessage, string botResponse)
    {
        var state = session.State;
        state.TurnCount++;

        var custMatch = CustomerIdPattern().Match(userMessage);
        if (custMatch.Success)
        {
            state.CustomerId = custMatch.Value;
        }
        else
        {
            custMatch = CustomerIdPattern().Match(botResponse);
            if (custMatch.Success && state.CustomerId is null)
            {
                state.CustomerId = custMatch.Value;
            }
        }

        var orderMatch = OrderIdPattern().Match(userMessage);
        if (orderMatch.Success)
        {
            state.CollectedInfo["LastMentionedOrderId"] = orderMatch.Value;
        }

        state.CurrentIntent = DetectUserIntent(userMessage);
        state.Phase = DetermineConversationPhase(state.TurnCount, botResponse);

        var (sentimentLabel, sentimentScore) = DetectSentiment(userMessage);
        state.Sentiment = sentimentLabel;
        state.SentimentScore = sentimentScore;

        state.SentimentHistory.Add(new SentimentEntry
        {
            Turn = state.TurnCount,
            Label = sentimentLabel,
            Score = sentimentScore
        });

        if (state.SentimentHistory.Count > 20)
            state.SentimentHistory.RemoveRange(0, state.SentimentHistory.Count - 20);

        if (sentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
            state.ConsecutiveNegativeTurns++;
        else
            state.ConsecutiveNegativeTurns = 0;

        UpdateSession(session);
    }

    // ─── IConversationStore ───

    public List<ChatMessage> GetHistory(string sessionId)
    {
        EnsureSessionHydrated(sessionId);
        if (_messageHistory.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return new List<ChatMessage>(history);
            }
        }
        return new List<ChatMessage>();
    }

    public void AddExchange(string sessionId, string userQuery, string assistantResponse)
    {
        EnsureSessionHydrated(sessionId);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ChatMessage>());
        lock (history)
        {
            history.Add(new ChatMessage(ChatRole.User, userQuery));
            history.Add(new ChatMessage(ChatRole.Assistant, assistantResponse));
        }

        var session = GetOrCreateSession(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try { InsertExchangeAsync(sessionId, userQuery, assistantResponse).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AddExchange INSERT başarısız. Session={Session}", sessionId);
        }

        ExtractAndUpdateState(sessionId, userQuery, assistantResponse);
    }

    public void AppendAssistantMessage(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        EnsureSessionHydrated(sessionId);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ChatMessage>());
        bool replacedLastEmpty;
        lock (history)
        {
            replacedLastEmpty =
                history.Count > 0
                && history[^1].Role == ChatRole.Assistant
                && string.IsNullOrEmpty(history[^1].Text);

            if (replacedLastEmpty)
            {
                history[^1] = new ChatMessage(ChatRole.Assistant, text);
            }
            else
            {
                history.Add(new ChatMessage(ChatRole.Assistant, text));
            }
        }

        var session = GetOrCreateSession(sessionId);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try
        {
            AppendAssistantMessageDbAsync(sessionId, text, replacedLastEmpty)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AppendAssistantMessage DB başarısız. Session={Session}", sessionId);
        }
    }

    public void ClearSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _messageHistory.TryRemove(sessionId, out _);
        _hydratedSessions.TryRemove(sessionId, out _);

        try { DeleteSessionAsync(sessionId).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] ClearSession DB başarısız. Session={Session}", sessionId);
        }
    }

    public List<SessionInfo> GetAllSessions()
    {
        EnsureAllListHydrated();

        var result = new List<SessionInfo>();
        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;
            var history = GetHistory(kvp.Key);
            var firstUserMsg = history.FirstOrDefault(m => m.Role == ChatRole.User)?.Text;

            result.Add(new SessionInfo
            {
                SessionId = session.SessionId,
                Title = firstUserMsg is not null
                    ? (firstUserMsg.Length > 50 ? firstUserMsg[..50] + "..." : firstUserMsg)
                    : WellKnown.FallbackMessages.NewChat,
                LastActivity = session.LastActivity,
                MessageCount = history.Count
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DB IO
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertSessionAsync(AgentSession session)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == session.SessionId);
        var stateJson = JsonSerializer.Serialize(session.State);

        if (existing is null)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = session.SessionId,
                CreatedAt = session.CreatedAt.Kind == DateTimeKind.Utc ? session.CreatedAt : session.CreatedAt.ToUniversalTime(),
                LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime(),
                StateJson = stateJson
            });
        }
        else
        {
            existing.LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime();
            existing.StateJson = stateJson;
        }

        await ctx.SaveChangesAsync();
    }

    private async Task InsertExchangeAsync(string sessionId, string userQuery, string assistantResponse)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        // Session'ı garanti altına al (FK için)
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        var now = DateTime.UtcNow;
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Text = userQuery,
            CreatedAt = now
        });
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = assistantResponse,
            CreatedAt = now.AddMilliseconds(1)
        });

        await ctx.SaveChangesAsync();
    }

    private async Task AppendAssistantMessageDbAsync(string sessionId, string text, bool replaceLastEmpty)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        if (replaceLastEmpty)
        {
            // Aynı session'daki son assistant mesajı boş ise UPDATE
            var lastAssistant = await ctx.Messages
                .Where(m => m.SessionId == sessionId && m.Role == "assistant")
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            if (lastAssistant is not null && string.IsNullOrEmpty(lastAssistant.Text))
            {
                lastAssistant.Text = text;
                lastAssistant.CreatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
                return;
            }
            // Bulunmazsa INSERT'e düş.
        }

        // Session ensure
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = text,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task DeleteSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Cascade: session silinirse mesajlar otomatik silinir (FK).
        var session = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null) return;
        ctx.Sessions.Remove(session);
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hydration
    // ─────────────────────────────────────────────────────────────────────────

    private void EnsureSessionHydrated(string sessionId)
    {
        if (_hydratedSessions.ContainsKey(sessionId)) return;
        if (!_hydratedSessions.TryAdd(sessionId, 0)) return;

        try { HydrateSessionAsync(sessionId).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Session] Hydrate başarısız. Id={Id}", sessionId);
        }
    }

    private async Task HydrateSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var sessionRow = await ctx.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (sessionRow is null) return;

        var state = string.IsNullOrWhiteSpace(sessionRow.StateJson) || sessionRow.StateJson == "{}"
            ? new SessionState()
            : JsonSerializer.Deserialize<SessionState>(sessionRow.StateJson) ?? new SessionState();

        _sessions[sessionId] = new AgentSession
        {
            SessionId = sessionRow.SessionId,
            CreatedAt = sessionRow.CreatedAt,
            LastActivity = sessionRow.LastActivity,
            State = state
        };

        var messages = await ctx.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var list = _messageHistory.GetOrAdd(sessionId, _ => new List<ChatMessage>());
        lock (list)
        {
            list.Clear();
            foreach (var m in messages)
            {
                var role = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.User
                    : ChatRole.Assistant;
                list.Add(new ChatMessage(role, m.Text));
            }
        }
    }

    private void EnsureAllListHydrated()
    {
        if (_allListHydrated) return;
        lock (_allHydrationLock)
        {
            if (_allListHydrated) return;
            try
            {
                HydrateAllSessionMetadataAsync().GetAwaiter().GetResult();
                _allListHydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] All-list hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAllSessionMetadataAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Sadece session metadata + ilk user message snippet alacağız.
        // Mesaj geçmişi tek tek lazy hydrate edilir (büyük session'larda gereksiz IO yapmamak için).
        var sessions = await ctx.Sessions.AsNoTracking()
            .OrderByDescending(s => s.LastActivity)
            .Take(500)
            .ToListAsync();

        foreach (var s in sessions)
        {
            if (_sessions.ContainsKey(s.SessionId)) continue;

            var state = string.IsNullOrWhiteSpace(s.StateJson) || s.StateJson == "{}"
                ? new SessionState()
                : JsonSerializer.Deserialize<SessionState>(s.StateJson) ?? new SessionState();

            _sessions[s.SessionId] = new AgentSession
            {
                SessionId = s.SessionId,
                CreatedAt = s.CreatedAt,
                LastActivity = s.LastActivity,
                State = state
            };
        }

        _logger.LogInformation("[Session] Metadata hydrate: {Count} session", sessions.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Yardımcı kurallar (InMemorySessionManager ile birebir aynı)
    // ─────────────────────────────────────────────────────────────────────────

    private static string DetectUserIntent(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("sipariş") &&
            (lower.Contains("durum") || lower.Contains("takip") || lower.Contains("nerede")))
        {
            return WellKnown.Intents.OrderInquiry;
        }

        foreach (var (intent, keywords) in WellKnown.IntentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k))) return intent;
        }

        return WellKnown.Intents.General;
    }

    private static string DetermineConversationPhase(int turnCount, string botResponse) =>
        turnCount switch
        {
            1 => WellKnown.Phases.Inquiry,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.SuccessMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Resolution,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.MissingInfoMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Inquiry,
            _ => WellKnown.Phases.Action
        };

    private static (string Label, double Score) DetectSentiment(string message)
    {
        var lower = message.ToLowerInvariant();
        foreach (var (sentiment, score, keywords) in WellKnown.SentimentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k))) return (sentiment, score);
        }
        return (WellKnown.Sentiments.Neutral, 0.5);
    }

    [GeneratedRegex(@"CUST-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex CustomerIdPattern();

    [GeneratedRegex(@"ORD-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex OrderIdPattern();
}
