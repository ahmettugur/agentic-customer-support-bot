// Services/Routing/SkillsBasedRouter.cs
// SkillsBasedRouter — Reasoning trace + müşteri profilinden skill etiketlerini
// Çıkarır, IHumanAgentRegistry'deki aday temsilciler arasında en iyi skill +
// Dil eşleşmesini bulur. LLM-siz, deterministik ve hızlı (<1ms).
//
// Skor Formülü:
//   skillMatch  = matchedSkills / max(requiredSkills, 1)            ∈ [0,1]
//   langMatch   = profile-language ∈ agent.Languages ? 1 : 0        ∈ {0,1}
//   loadFactor  = 1 - (currentLoad / maxLoad)                        ∈ [0,1]
//   priorityBoost = agent.Priority * 0.05                            (cap +0.2)
//   score = (1 - LanguageWeight) * skillMatch + LanguageWeight * langMatch
//   tie-break: yüksek loadFactor + Priority + son atama eskiliği

using CustomerSupportBot.Api.Models;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Services.Routing;

public class SkillsBasedRouter : ISkillsBasedRouter
{
    private readonly IHumanAgentRegistry _registry;
    private readonly RoutingOptions _options;

    public SkillsBasedRouter(IHumanAgentRegistry registry, IOptions<RoutingOptions> options)
    {
        _registry = registry;
        _options = options.Value;
    }

    public RoutingDecision Decide(
        ReasoningTrace trace,
        string? agentName,
        Models.Memory.CustomerProfile? customerProfile)
    {
        if (!_options.Enabled)
        {
            return new RoutingDecision { Note = "Smart routing devre dışı." };
        }

        var requiredSkills = ExtractRequiredSkills(trace, agentName, customerProfile);
        var preferredLanguage = (customerProfile?.PreferredLanguage ?? "tr").Trim().ToLowerInvariant();
        var langWeight = Math.Clamp(_options.LanguageWeight, 0.0, 1.0);

        var candidates = _registry.GetActive()
            .Where(a => a.CurrentLoad < a.MaxConcurrentLoad)
            .ToList();

        if (candidates.Count == 0)
        {
            return new RoutingDecision
            {
                MatchedSkills = new(),
                MissingSkills = requiredSkills.ToList(),
                Note = "Müsait temsilci yok (hepsi pasif veya kapasite dolu)."
            };
        }

        HumanAgent? bestAgent = null;
        double bestScore = -1;
        List<string>? bestMatched = null;
        List<string>? bestMissing = null;

        foreach (var agent in candidates)
        {
            var (matched, missing, skillMatch) = ScoreSkillMatch(agent.Skills, requiredSkills);
            var langMatch = AgentSpeaksLanguage(agent, preferredLanguage) ? 1.0 : 0.0;
            var loadFactor = 1.0 - ((double)agent.CurrentLoad / Math.Max(agent.MaxConcurrentLoad, 1));
            var priorityBoost = Math.Min(agent.Priority * 0.05, 0.2);

            var score = (1 - langWeight) * skillMatch
                      + langWeight * langMatch
                      + priorityBoost;

            if (_options.LoadBalancingEnabled)
            {
                score += loadFactor * 0.05; // tie-break, hafif etki
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAgent = agent;
                bestMatched = matched;
                bestMissing = missing;
            }
        }

        if (bestAgent == null || bestScore < _options.MinMatchScore)
        {
            return new RoutingDecision
            {
                MatchedSkills = bestMatched ?? new(),
                MissingSkills = bestMissing ?? requiredSkills.ToList(),
                MatchScore = Math.Max(bestScore, 0),
                Note = "Yeterli skill match yok; manuel atama gerekli."
            };
        }

        return new RoutingDecision
        {
            SuggestedAgentId = bestAgent.Id,
            SuggestedAgentName = bestAgent.DisplayName,
            MatchScore = Math.Round(Math.Clamp(bestScore, 0, 1), 3),
            MatchedSkills = bestMatched ?? new(),
            MissingSkills = bestMissing ?? new(),
            Note = BuildNote(bestAgent, bestMatched ?? new(), preferredLanguage, requiredSkills)
        };
    }

    /// <summary>
    /// Reasoning trace + müşteri profilinden gerekli skill tag listesi üretir.
    /// 1. Final intent (Reasoning.Intent veya Planning.DetectedIntent) → IntentSkillMap
    /// 2. Specialist agent adı → tematik skill (complaint/order/product)
    /// 3. Müşteri profili admin notu → ProfileKeywordSkillMap
    /// 4. Tercih edilen dil → "tr"/"en"
    /// </summary>
    public List<string> ExtractRequiredSkills(
        ReasoningTrace trace,
        string? agentName,
        Models.Memory.CustomerProfile? customerProfile)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        // 1. Intent → skills
        var intent = trace?.Reasoning?.Intent ?? trace?.Planning?.DetectedIntent ?? "";
        if (!string.IsNullOrWhiteSpace(intent))
        {
            if (_options.IntentSkillMap.TryGetValue(intent, out var skills))
            {
                foreach (var s in skills) AddNorm(result, s);
            }
        }

        // 2. Agent adı → tematik skill
        if (!string.IsNullOrWhiteSpace(agentName))
        {
            if (agentName.Contains("Complaint", StringComparison.OrdinalIgnoreCase))
                result.Add("complaint");
            else if (agentName.Contains("OrderPlacement", StringComparison.OrdinalIgnoreCase))
                result.Add("order");
            else if (agentName.Contains("OrderInquiry", StringComparison.OrdinalIgnoreCase))
                result.Add("order");
            else if (agentName.Contains("Product", StringComparison.OrdinalIgnoreCase))
                result.Add("product");
        }

        // 3. Admin notu / profil keyword
        if (!string.IsNullOrWhiteSpace(customerProfile?.AdminNote))
        {
            foreach (var kvp in _options.ProfileKeywordSkillMap)
            {
                if (customerProfile.AdminNote.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    AddNorm(result, kvp.Value);
            }
        }

        // 4. Dil
        var preferredLang = customerProfile?.PreferredLanguage;
        if (!string.IsNullOrWhiteSpace(preferredLang))
            AddNorm(result, preferredLang);

        return result.ToList();
    }

    private static (List<string> matched, List<string> missing, double score) ScoreSkillMatch(
        IReadOnlyList<string> agentSkills,
        IReadOnlyList<string> requiredSkills)
    {
        if (requiredSkills.Count == 0)
            return (new(), new(), 0.5); // tarafsız skor — required boşsa hiçbir aday avantajlı değil

        var agentSet = new HashSet<string>(agentSkills, StringComparer.Ordinal);
        var matched = requiredSkills.Where(s => agentSet.Contains(s)).ToList();
        var missing = requiredSkills.Where(s => !agentSet.Contains(s)).ToList();
        var score = (double)matched.Count / requiredSkills.Count;
        return (matched, missing, score);
    }

    private static bool AgentSpeaksLanguage(HumanAgent agent, string preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(preferredLanguage)) return true;
        if (agent.Languages == null || agent.Languages.Count == 0)
            return preferredLanguage == "tr"; // dil belirtilmemişse "tr" varsayalım
        return agent.Languages.Contains(preferredLanguage, StringComparer.Ordinal);
    }

    private static string BuildNote(HumanAgent agent, List<string> matched, string lang, List<string> required)
    {
        if (matched.Count == 0 && required.Count == 0)
            return $"Müsait temsilci: {agent.DisplayName} ({lang}).";
        if (matched.Count == 0)
            return $"Skill match yok; load balancing ile {agent.DisplayName} seçildi.";
        return $"Eşleşen skill'ler: {string.Join(", ", matched)} → {agent.DisplayName}.";
    }

    private static void AddNorm(HashSet<string> set, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        set.Add(tag.Trim().ToLowerInvariant());
    }
}
