// Ports/Outbound/AgentRoutingOptions.cs
// İlk ajanı kim seçer: PlanningAgent (LLM) mı, Reasoning'in niyetinden türeyen deterministik tablo mu?

namespace CustomerSupportBot.Application.Ports.Outbound;

public enum AgentRoutingMode
{
    /// <summary>Eski davranış: ilk ajan her zaman PlanningAgent (tur başına bir ana-model çağrısı).</summary>
    Planner,

    /// <summary>
    /// Planning karar verir (davranış DEĞİŞMEZ); yönlendirici yanında kendi kararını üretip Planning'inkiyle karşılaştırır ve sayar
    /// (<c>agent.routing.decisions{mode=shadow,outcome=match|mismatch|…}</c>). Geçişten önce uyuşma oranını GERÇEK trafikte ölçmek içindir.
    /// </summary>
    Shadow,

    /// <summary>
    /// Yönlendirici karar verebiliyorsa ilk ajan doğrudan odur ve Planning ATLANIR; karar veremiyorsa (susarsa) Planning çalışır.
    /// </summary>
    Deterministic
}

public sealed class AgentRoutingOptions
{
    public const string SectionName = "AgentRouting";

    /// <summary>Varsayılan <see cref="AgentRoutingMode.Shadow"/>: davranışı değiştirmeden ölçer. Geçiş bilinçli bir yapılandırma değişikliğidir.</summary>
    public AgentRoutingMode Mode { get; set; } = AgentRoutingMode.Shadow;
}
