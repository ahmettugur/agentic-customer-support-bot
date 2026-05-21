using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Session başına chat modunu tutan secondary port.
///</summary>
public interface IChatModeRegistry
{
    ChatMode GetMode(string sessionId);
    ChatSessionState? GetState(string sessionId);
    bool TakeOver(string sessionId, string? humanAgent);
    bool Release(string sessionId);
    IReadOnlyList<ChatSessionState> GetActive();

    event EventHandler<ChatSessionState>? ModeChanged;
}
