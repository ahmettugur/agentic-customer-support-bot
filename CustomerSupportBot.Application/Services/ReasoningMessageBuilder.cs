// Application/Services/ReasoningMessageBuilder.cs
// ReasoningService LLM çağrısı için system prompt + history + user query mesaj listesi kurar.

using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Reasoning LLM'ine gönderilecek mesajları (system prompt + history + user query)
/// kurar. Verified entity bloğunu, history note'unu ve session state'ini
/// prompt template'e enjekte eder.
/// </summary>
public class ReasoningMessageBuilder
{
    private readonly IPromptRepository _prompts;

    public ReasoningMessageBuilder(IPromptRepository prompts)
    {
        _prompts = prompts;
    }

    /// <summary>
    /// Reasoning çağrısı için tam mesaj listesini kurar.
    /// </summary>
    public List<ConversationMessage> Build(
        string query,
        AgentSession session,
        List<ConversationMessage>? history,
        VerifiedEntities verified)
    {
        var systemPrompt = BuildSystemPrompt(session, verified, hasHistory: history is { Count: > 0 });

        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.System, systemPrompt)
        };

        if (history is { Count: > 0 })
        {
            messages.AddRange(history);
        }

        messages.Add(new ConversationMessage(ConversationRoles.User, query));
        return messages;
    }

    private string BuildSystemPrompt(AgentSession session, VerifiedEntities verified, bool hasHistory)
    {
        var stateInfo = $"CustomerId: {session.State.CustomerId ?? WellKnown.Intents.Unknown}, " +
                        $"Phase: {session.State.Phase}, " +
                        $"TurnCount: {session.State.TurnCount}";

        var historyNote = hasHistory ? _prompts.Get("services/reasoning-history-note") : "";
        var verifiedBlock = EntityVerifier.BuildPromptBlock(verified) ?? "";

        return _prompts.Render("services/reasoning-system", new Dictionary<string, string?>
        {
            ["STATE_INFO"] = stateInfo,
            ["HISTORY_NOTE"] = historyNote,
            ["VERIFIED_ENTITIES"] = verifiedBlock
        });
    }
}
