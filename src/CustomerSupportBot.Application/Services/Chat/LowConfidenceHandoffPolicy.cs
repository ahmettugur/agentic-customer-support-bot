// Application/Services/Chat/LowConfidenceHandoffPolicy.cs
// Bot art arda isteği anlayamıyorsa insana devir: önce temsilci önerisi (chip), ardışık olursa otomatik eskalasyon.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

public sealed class LowConfidenceHandoffOptions
{
    public const string SectionName = "LowConfidenceHandoff";

    public bool Enabled { get; set; } = true;

    /// <summary>Bu kadar ARDIŞIK düşük güvenli turda eskalasyon açılır; daha azında yalnızca temsilci önerilir.</summary>
    public int ConsecutiveTurnsForEscalation { get; set; } = 2;
}

public enum HandoffDecision { None, Offer, Escalate }

public static class LowConfidenceHandoffPolicy
{
    public static HandoffDecision Decide(
        int lowConfidenceTurns, string? intent, ChatMode mode, bool hasOpenEscalation, LowConfidenceHandoffOptions options)
    {
        if (!options.Enabled || lowConfidenceTurns <= 0) return HandoffDecision.None;
        if (mode == ChatMode.Human || hasOpenEscalation) return HandoffDecision.None;
        // Açık temsilci talebinin kendi yolu (HumanHandoffAgent) var; burada ikinci bir eskalasyon açılmaz.
        if (intent == WellKnown.Intents.HumanHandoffRequest) return HandoffDecision.None;

        return lowConfidenceTurns >= Math.Max(1, options.ConsecutiveTurnsForEscalation)
            ? HandoffDecision.Escalate
            : HandoffDecision.Offer;
    }
}
