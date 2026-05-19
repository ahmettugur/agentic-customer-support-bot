// Application/Services/Routing/ISkillsBasedRouter.cs
// Eskalasyon için en uygun insan müşteri temsilcisini öneren router interface'i.

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
    /// Eskalasyon için skill gereksinimlerini ve önerilen temsilciyi hesaplar.
    /// Hiç temsilci yoksa veya skor eşik altında kalırsa SuggestedAgentId null döner
    /// (eskalasyon yine kaydedilir, admin manuel atayabilir).
    /// </summary>
    /// <param name="trace">İlgili workflow reasoning trace'i.</param>
    /// <param name="agentName">Eskalasyona neden olan specialist agent (örn: ComplaintAgent).</param>
    /// <param name="customerProfile">Varsa müşteri profili (dil/ton tercihi, admin notu).</param>
    RoutingDecision Decide(
        ReasoningTrace trace,
        string? agentName,
        CustomerProfile? customerProfile);
}
