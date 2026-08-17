// Application/Services/ReasoningMessageBuilder.cs
// ReasoningService LLM çağrısı için system prompt + history + user query mesaj listesi kurar.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Reasoning;

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

        // Admin "Yeniden Planla" tetiklediyse override hint'ini reasoning agent da görsün.
        // (State temizliği Planning aşamasında yapılır — burada sadece okuruz.)
        if (session.State.ForceReplanNextTurn)
        {
            var hint = WellKnown.FallbackMessages.ReplanPlanningHint;
            if (!string.IsNullOrWhiteSpace(session.State.ReplanNote))
                hint += $"\n\n📌 Admin notu: \"{session.State.ReplanNote}\"";
            messages.Add(new ConversationMessage(ConversationRoles.System, hint));
        }

        messages.Add(new ConversationMessage(ConversationRoles.User, query));
        return messages;
    }

    private string BuildSystemPrompt(AgentSession session, VerifiedEntities verified, bool hasHistory)
    {
        // AuthenticatedCustomerId (JWT) — State.CustomerId DEĞİL. İkincisi LLM'in kullanıcı
        // metninden çıkardığı, kullanıcının "ben 1008 numaralı müşteriyim" diyerek
        // değiştirebildiği alandır; reasoning'e onu vermek ajanı yanlış kimlik üzerinden
        // akıl yürütmeye iter (bkz. CustomerContextProvider'daki aynı düzeltme).
        var stateInfo = $"CustomerId: {session.State.AuthenticatedCustomerId ?? WellKnown.Intents.Unknown}, " +
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
