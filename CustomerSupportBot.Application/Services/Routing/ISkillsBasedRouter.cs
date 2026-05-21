using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Routing;

/// <summary>
/// Eskalasyon için en uygun insan müşteri temsilcisini öneren router.
/// Reasoning trace + opsiyonel müşteri profili üzerinden skill gereksinimlerini
/// çıkarır, registry'deki adaylar arasında skor hesaplar ve en iyi match'i döner.
/// </summary>
public interface ISkillsBasedRouter
{
    /// <summary>
    /// Hiç temsilci yoksa veya skor eşik altında kalırsa SuggestedAgentId null döner
    /// (eskalasyon yine kaydedilir, admin manuel atayabilir).
    /// </summary>
    RoutingDecision Decide(
        ReasoningTrace trace,
        string? agentName,
        CustomerProfile? customerProfile);
}
