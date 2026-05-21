// Adapters.AI/Chat/ReasoningChatClient.cs
// Reasoning model (o-series) için IReasoningChatClient implementasyonu.
// IChatClient'ı port sınırında sararak ConversationMessage ↔ ChatMessage dönüşümü yapar.

using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.AI.Chat;

/// <summary>
/// Reasoning model için IReasoningChatClient implementasyonu.
/// ConversationMessage domain tiplerini adapte ederek Microsoft.Extensions.AI'ye iletir.
/// </summary>
public class ReasoningChatClient : IReasoningChatClient
{
    private readonly IChatClient _client;
    public string ModelName { get; }
    public string ReasoningEffort { get; }

    public ReasoningChatClient(IChatClient client, string modelName, string reasoningEffort)
    {
        _client = client;
        ModelName = modelName;
        ReasoningEffort = reasoningEffort;
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken ct = default)
    {
        try
        {
            var chatMessages = Map(messages);
            var options = BuildOptions();
            var response = await _client.GetResponseAsync(chatMessages, options, ct);
            return response.Text ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested is false)
        {
            throw ExceptionTranslator.Translate(ex, "ReasoningChatClient.CompleteAsync başarısız.");
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ConversationMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        IAsyncEnumerable<ChatResponseUpdate> stream;
        try
        {
            var chatMessages = Map(messages);
            var options = BuildOptions();
            stream = _client.GetStreamingResponseAsync(chatMessages, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested is false)
        {
            throw ExceptionTranslator.Translate(ex, "ReasoningChatClient.StreamAsync başarısız.");
        }

        await foreach (var update in stream)
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    private ChatOptions BuildOptions() => new()
    {
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [WellKnown.ReasoningEffort.PropertyKey] = ReasoningEffort
        }
    };

    internal static IList<ChatMessage> Map(IReadOnlyList<ConversationMessage> messages)
        => messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text)).ToList();

    private static ChatRole RoleFor(string role) => role switch
    {
        ConversationRoles.User      => ChatRole.User,
        ConversationRoles.System    => ChatRole.System,
        _                           => ChatRole.Assistant
    };
}
