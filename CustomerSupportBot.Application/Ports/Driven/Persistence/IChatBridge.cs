// Ports/Driven/Persistence/IChatBridge.cs
// SECONDARY PORT — HITL Live Takeover mesaj köprüsü.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// HITL Live Takeover için secondary port.
/// User ↔ Admin arası mesaj köprüsü.
/// Adaptörler: PostgresChatBridge, InMemoryChatBridge.
/// </summary>
public interface IChatBridge
{
    void PublishUserMessage(string sessionId, string text);
    void PublishAdminMessage(string sessionId, string humanAgent, string text);
    void PublishSystemMessage(string sessionId, string text);
    void PublishBotMessage(string sessionId, string text);
    void PublishBotTyping(string sessionId, bool on);
    void RecordBotExchange(string sessionId, string userQuery, string botResponse);

    IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct);
    IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct);
    IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50);
    void Reset(string sessionId);
}
