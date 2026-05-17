// Services/InMemoryChatModeRegistry.cs
// HITL Live Takeover — thread-safe in-memory kip registry.
// Production'da Redis pub/sub + persistence ile değiştirilebilir.

using System.Collections.Concurrent;
using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

public class InMemoryChatModeRegistry : IChatModeRegistry
{
    private readonly ConcurrentDictionary<string, ChatSessionState> _states = new();
    private readonly ILogger<InMemoryChatModeRegistry> _logger;

    public event EventHandler<ChatSessionState>? ModeChanged;

    public InMemoryChatModeRegistry(ILogger<InMemoryChatModeRegistry> logger)
    {
        _logger = logger;
    }

    public ChatMode GetMode(string sessionId) =>
        _states.TryGetValue(sessionId, out var s) ? s.Mode : ChatMode.Bot;

    public ChatSessionState? GetState(string sessionId) =>
        _states.TryGetValue(sessionId, out var s) ? s : null;

    public bool TakeOver(string sessionId, string? humanAgent)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        var agent = humanAgent ?? WellKnown.Defaults.Admin;

        // Zaten başka biri Human modtaysa reddet — concurrent takeover önlemi.
        // Aynı agent yeniden çağırırsa (reconnect vb.) izin ver.
        if (_states.TryGetValue(sessionId, out var current)
            && current.Mode == ChatMode.Human
            && !string.Equals(current.HumanAgent, agent, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[HITL] TakeOver reddedildi: session={Session} zaten {Existing} tarafından alındı. İstekte bulunan: {Requester}",
                sessionId, current.HumanAgent, agent);
            return false;
        }

        var state = _states.AddOrUpdate(
            sessionId,
            _ => new ChatSessionState
            {
                SessionId = sessionId,
                Mode = ChatMode.Human,
                HumanAgent = agent,
                EnteredAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.Mode = ChatMode.Human;
                existing.HumanAgent = agent;
                existing.EnteredAt ??= DateTime.UtcNow;
                existing.LastActivityAt = DateTime.UtcNow;
                return existing;
            });

        _logger.LogInformation(
            "[HITL] TakeOver: session={Session}, agent={Agent}",
            sessionId, state.HumanAgent);

        FireChanged(state);
        return true;
    }

    public bool Release(string sessionId)
    {
        if (!_states.TryGetValue(sessionId, out var state)) return false;
        if (state.Mode == ChatMode.Bot) return false;

        state.Mode = ChatMode.Bot;
        state.HumanAgent = null;
        state.EnteredAt = null;
        state.LastActivityAt = DateTime.UtcNow;

        _logger.LogInformation("[HITL] Release: session={Session}", sessionId);
        FireChanged(state);
        return true;
    }

    public IReadOnlyList<ChatSessionState> GetActive() =>
        _states.Values
            .Where(s => s.Mode == ChatMode.Human)
            .OrderByDescending(s => s.EnteredAt ?? DateTime.MinValue)
            .ToList();

    private void FireChanged(ChatSessionState state)
    {
        try { ModeChanged?.Invoke(this, state); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModeChanged handler failed");
        }
    }
}
