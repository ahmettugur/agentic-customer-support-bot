// Ports/Driven/Persistence/IChatModeRegistry.cs
// SECONDARY PORT — Session chat modu (Bot | Human) yönetimi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Session başına chat modunu tutan secondary port.
/// Adaptörler: PostgresChatModeRegistry, InMemoryChatModeRegistry.
/// </summary>
public interface IChatModeRegistry
{
    ChatMode GetMode(string sessionId);
    ChatSessionState? GetState(string sessionId);
    bool TakeOver(string sessionId, string? humanAgent);
    bool Release(string sessionId);
    IReadOnlyList<ChatSessionState> GetActive();

    event EventHandler<ChatSessionState>? ModeChanged;
}
