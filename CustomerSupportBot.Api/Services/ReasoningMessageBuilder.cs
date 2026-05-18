// Services/ReasoningMessageBuilder.cs
// ReasoningService LLM çağrısı için system prompt + history + user query mesaj listesi kurar.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Reasoning LLM'ine gönderilecek mesajları (system prompt + history + user query)
/// kurar. Verified entity bloğunu, history note'unu ve session state'ini
/// prompt template'e enjekte eder.
/// </summary>
public class ReasoningMessageBuilder
{
    private readonly PromptService _prompts;

    public ReasoningMessageBuilder(PromptService prompts)
    {
        _prompts = prompts;
    }

    /// <summary>
    /// Reasoning çağrısı için tam mesaj listesini kurar.
    /// </summary>
    public List<ChatMessage> Build(
        string query,
        AgentSession session,
        List<ChatMessage>? history,
        VerifiedEntities verified)
    {
        var systemPrompt = BuildSystemPrompt(session, verified, hasHistory: history is { Count: > 0 });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };

        if (history is { Count: > 0 })
        {
            messages.AddRange(history);
        }

        messages.Add(new ChatMessage(ChatRole.User, query));
        return messages;
    }

    /// <summary>
    /// Reasoning system prompt'unu render eder. Session state, history note ve
    /// verified entity bloğunu placeholder'lara enjekte eder.
    /// </summary>
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

