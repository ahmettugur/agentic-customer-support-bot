// Ports/Driven/Persistence/IChatModeRepository.cs
// SECONDARY PORT — Session chat modu (Bot | Human) yönetimi.
// Mevcut IChatModeRegistry interface'i bu port'a taşınır.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Session başına chat modunu tutan secondary port.
/// Adaptörler: PostgresChatModeRegistry, InMemoryChatModeRegistry.
/// </summary>
public interface IChatModeRepository
{
    ChatMode GetMode(string sessionId);
    ChatSessionState? GetState(string sessionId);
    bool TakeOver(string sessionId, string? humanAgent);
    bool Release(string sessionId);
    IReadOnlyList<ChatSessionState> GetActive();

    event EventHandler<ChatSessionState>? ModeChanged;
}
