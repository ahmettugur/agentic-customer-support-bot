// Application/Services/Reasoning/IntentRouter.cs
// Deterministik ilk-ajan yönlendirmesi. Reasoning niyeti zaten karar verdi; PlanningAgent çoğu turda aynı kararı (niyet → ajan)
// ana modelle yeniden üretiyordu. Bu tablo aynı kararı LLM'siz verir. Emin olamadığında SUSAR — çağıran eski Planning yoluna düşer.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Reasoning;

/// <summary><c>Agent == null</c> ise yönlendirici susmuştur (<see cref="Reason"/> nedenini söyler).</summary>
public sealed record RouteDecision(string? Agent, string Reason)
{
    public bool Decided => Agent is not null;

    public static RouteDecision Abstain(string reason) => new(null, reason);
}

public static class IntentRouter
{
    /// <summary>Niyet → ilk ajan. Anahtarlar sistemde TAM string eşleşmesiyle kullanılır (bkz. reasoning-system.md), bu yüzden büyük/küçük harf duyarlı.</summary>
    private static readonly IReadOnlyDictionary<string, string> ByIntent = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [WellKnown.Intents.OrderCreation] = WellKnown.AgentNames.Order,
        [WellKnown.Intents.OrderInquiry] = WellKnown.AgentNames.Order,
        [WellKnown.Intents.OrderListing] = WellKnown.AgentNames.Order,
        [WellKnown.Intents.OrderCancellation] = WellKnown.AgentNames.Order,
        [WellKnown.Intents.ReturnRequest] = WellKnown.AgentNames.Order,
        [WellKnown.Intents.Complaint] = WellKnown.AgentNames.Complaint,
        [WellKnown.Intents.ProductInfo] = WellKnown.AgentNames.Product,
        [WellKnown.Intents.HumanHandoffRequest] = WellKnown.AgentNames.HumanHandoff,
        // Selamlama, konu dışı, enjeksiyon denemesi: uzman gerekmez; ResponseAgent ön-analizden yanıtı kendisi yazar.
        [WellKnown.Intents.General] = WellKnown.AgentNames.Response
    };

    public static RouteDecision Decide(ReasoningResult? reasoning)
    {
        if (reasoning is null) return RouteDecision.Abstain("no_reasoning");

        // Alt görev koşusu: hedef ajan bölme anında zaten seçildi (SubTaskOrchestrator); niyet/eksik bilgi onu değiştirmez.
        if (!string.IsNullOrWhiteSpace(reasoning.ConstrainedTargetAgent))
            return new RouteDecision(reasoning.ConstrainedTargetAgent, "subtask");

        // Reasoning hiç çalışmadıysa (zaman aşımı/hata yer tutucusu) elde gerçek bir karar yok.
        if (reasoning.IsFallback) return RouteDecision.Abstain("fallback");

        if (!ByIntent.TryGetValue(reasoning.Intent ?? "", out var agent))
            return RouteDecision.Abstain("unknown_intent");

        // Zorunlu bilgi eksikse Planning needsClarification=true ile ResponseAgent'a yönlendiriyordu: soru tek mesajda sorulur,
        // uzman boşuna çağrılmaz. Temsilci talebi bundan etkilenmez (hiçbir alan zorunlu değil).
        if (agent != WellKnown.AgentNames.Response && agent != WellKnown.AgentNames.HumanHandoff
            && reasoning.RequiredInfo.Any(IsActuallyMissing))
            return new RouteDecision(WellKnown.AgentNames.Response, "required_info");

        return new RouteDecision(agent, "intent");
    }

    /// <summary>Müşteri kimliği girişten bilinir; model yanlışlıkla "eksik" listesine yazsa bile netleştirme sebebi değildir.</summary>
    private static bool IsActuallyMissing(string requiredInfo)
    {
        if (string.IsNullOrWhiteSpace(requiredInfo)) return false;
        var s = requiredInfo.ToLowerInvariant();
        return !(s.Contains("customer_id") || s.Contains("customer id") || s.Contains("müşteri_kimli") || s.Contains("müşteri kimli")
                 || s.Contains("müşteri_numara") || s.Contains("müşteri numara"));
    }
}

/// <summary>Gölge modda yönlendiricinin kararını PlanningAgent'ın gerçek kararıyla karşılaştırır (saf).</summary>
public static class RoutingComparison
{
    public const string Match = "match";
    public const string Mismatch = "mismatch";
    public const string RouterAbstained = "router_abstained";
    public const string Unobserved = "unobserved";

    public readonly record struct Result(string Outcome, string PlannerAgent);

    public static Result Compare(RouteDecision router, PlanningResult? plan)
    {
        if (plan is null) return new Result(Unobserved, "-");

        // needsClarification=true iken selectedAgent ne olursa olsun routing ResponseAgent'a gider (bkz. PlanRoutingStrategy).
        var planner = plan.NeedsClarification ? WellKnown.AgentNames.Response : Normalize(plan.SelectedAgent);

        if (!router.Decided) return new Result(RouterAbstained, planner);
        return new Result(string.Equals(router.Agent, planner, StringComparison.OrdinalIgnoreCase) ? Match : Mismatch, planner);
    }

    /// <summary>Metrik etiketi kontrolsüz büyümesin: yalnızca bilinen ajan adları olduğu gibi geçer.</summary>
    private static string Normalize(string? agent) =>
        WellKnown.AgentNames.All.FirstOrDefault(a => string.Equals(a, agent, StringComparison.OrdinalIgnoreCase)) ?? "unknown";
}
