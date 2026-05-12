// Services/InMemoryChatModeRegistry.cs
// HITL Live Takeover — thread-safe in-memory kip registry.
// Production'da Redis pub/sub + persistence ile değiştirilebilir.

using System.Collections.Concurrent;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

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
