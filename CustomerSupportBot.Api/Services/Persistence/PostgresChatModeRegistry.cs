// Services/Persistence/PostgresChatModeRegistry.cs
// HITL Live Takeover — hibrit cache + PostgreSQL kip registry.
//
// Davranış:
//   - In-memory ConcurrentDictionary state'i + ModeChanged event aynen korunur
//     (chat SSE loop'u event'lere bağlı; süreç içi pub/sub).
//   - TakeOver/Release/MessageCount güncellemeleri DB'ye write-through.
//   - Cache lazy hydrate: ilk erişimde DB'deki Human ve Bot tüm kayıtları yüklenir.
//   - Singleton servis ⇒ DbContext IDbContextFactory ile açılır.

using System.Collections.Concurrent;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Chat;
using CustomerSupportBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed class PostgresChatModeRegistry : IChatModeRegistry
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresChatModeRegistry> _logger;
    private readonly ConcurrentDictionary<string, ChatSessionState> _states = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public event EventHandler<ChatSessionState>? ModeChanged;

    public PostgresChatModeRegistry(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresChatModeRegistry> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public ChatMode GetMode(string sessionId)
    {
        EnsureHydrated();
        return _states.TryGetValue(sessionId, out var s) ? s.Mode : ChatMode.Bot;
    }

    public ChatSessionState? GetState(string sessionId)
    {
        EnsureHydrated();
        return _states.TryGetValue(sessionId, out var s) ? s : null;
    }

    public bool TakeOver(string sessionId, string? humanAgent)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        EnsureHydrated();

        var state = _states.AddOrUpdate(
            sessionId,
            _ => new ChatSessionState
            {
                SessionId = sessionId,
                Mode = ChatMode.Human,
                HumanAgent = humanAgent ?? WellKnown.Defaults.Admin,
                EnteredAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.Mode = ChatMode.Human;
                existing.HumanAgent = humanAgent ?? existing.HumanAgent ?? WellKnown.Defaults.Admin;
                existing.EnteredAt ??= DateTime.UtcNow;
                existing.LastActivityAt = DateTime.UtcNow;
                return existing;
            });

        try { UpsertAsync(state).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] TakeOver DB UPSERT başarısız. Session={Session}", sessionId);
            throw;
        }

        _logger.LogInformation(
            "[HITL] TakeOver: session={Session}, agent={Agent}",
            sessionId, state.HumanAgent);

        FireChanged(state);
        return true;
    }

    public bool Release(string sessionId)
    {
        EnsureHydrated();
        if (!_states.TryGetValue(sessionId, out var state)) return false;
        if (state.Mode == ChatMode.Bot) return false;

        state.Mode = ChatMode.Bot;
        state.HumanAgent = null;
        state.EnteredAt = null;
        state.LastActivityAt = DateTime.UtcNow;

        try { UpsertAsync(state).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Release DB UPSERT başarısız. Session={Session}", sessionId);
            throw;
        }

        _logger.LogInformation("[HITL] Release: session={Session}", sessionId);
        FireChanged(state);
        return true;
    }

    public IReadOnlyList<ChatSessionState> GetActive()
    {
        EnsureHydrated();
        return _states.Values
            .Where(s => s.Mode == ChatMode.Human)
            .OrderByDescending(s => s.EnteredAt ?? DateTime.MinValue)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(ChatSessionState state)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var existing = await ctx.ChatSessionModes
            .FirstOrDefaultAsync(m => m.SessionId == state.SessionId);

        if (existing is null)
        {
            ctx.ChatSessionModes.Add(new ChatSessionModeEntity
            {
                SessionId = state.SessionId,
                Mode = state.Mode.ToString(),
                HumanAgent = state.HumanAgent,
                EnteredAt = state.EnteredAt,
                LastActivityAt = state.LastActivityAt,
                MessageCount = state.MessageCount
            });
        }
        else
        {
            existing.Mode = state.Mode.ToString();
            existing.HumanAgent = state.HumanAgent;
            existing.EnteredAt = state.EnteredAt;
            existing.LastActivityAt = state.LastActivityAt;
            existing.MessageCount = state.MessageCount;
        }

        await ctx.SaveChangesAsync();
    }

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] ChatMode cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.ChatSessionModes.AsNoTracking().ToListAsync();

        foreach (var e in rows)
        {
            var mode = Enum.TryParse<ChatMode>(e.Mode, ignoreCase: true, out var m)
                ? m : ChatMode.Bot;

            _states[e.SessionId] = new ChatSessionState
            {
                SessionId = e.SessionId,
                Mode = mode,
                HumanAgent = e.HumanAgent,
                EnteredAt = e.EnteredAt,
                LastActivityAt = e.LastActivityAt,
                MessageCount = e.MessageCount
            };
        }

        _logger.LogInformation("[HITL] ChatMode cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private void FireChanged(ChatSessionState state)
    {
        try { ModeChanged?.Invoke(this, state); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModeChanged handler failed");
        }
    }
}
