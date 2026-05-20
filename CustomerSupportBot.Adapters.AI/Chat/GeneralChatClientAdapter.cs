// Adapters.AI/Chat/GeneralChatClientAdapter.cs
// IGeneralChatClient implementasyonu — IChatClient'ı port sınırında sarar.
// ConversationMessage ↔ ChatMessage dönüşümünü burada yapar; core temiz kalır.

using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.AI.Chat;

/// <summary>
/// Genel amaçlı LLM tamamlama için IGeneralChatClient implementasyonu.
/// </summary>
public sealed class GeneralChatClientAdapter : IGeneralChatClient
{
    private readonly IChatClient _client;

    public GeneralChatClientAdapter(IChatClient client)
    {
        _client = client;
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken ct = default)
    {
        var chatMessages = messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text)).ToList();
        var response = await _client.GetResponseAsync(chatMessages, cancellationToken: ct);
        return response.Text ?? "";
    }

    private static ChatRole RoleFor(string role) => role switch
    {
        ConversationRoles.User   => ChatRole.User,
        ConversationRoles.System => ChatRole.System,
        _                        => ChatRole.Assistant
    };
}
