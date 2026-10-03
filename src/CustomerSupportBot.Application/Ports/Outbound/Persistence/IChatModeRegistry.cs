using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Session başına chat modunu tutan secondary port.
///</summary>
public interface IChatModeRegistry
{
    ChatMode GetMode(string sessionId);
    ChatSessionState? GetState(string sessionId);
    /// <summary>Yazma bilinçli olarak CancellationToken almaz: istemci bağlantıyı kesse bile cache ile DB tutarlı kalmalı.</summary>
    Task<bool> TakeOverAsync(string sessionId, string? humanAgent);
    Task<bool> ReleaseAsync(string sessionId);
    IReadOnlyList<ChatSessionState> GetActive();

    event EventHandler<ChatSessionState>? ModeChanged;
}
