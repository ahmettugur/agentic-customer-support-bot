# SkillsBasedRouter

**Dosya:** `Services/Routing/SkillsBasedRouter.cs`  
**Implements:** `ISkillsBasedRouter`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Eskalasyon oluşturulduğunda hangi human agent'ın oturumu devralması gerektiğini belirler. LLM kullanmaz — deterministik, <1ms çalışır. Skill eşleşmesi, dil tercihi ve mevcut yük faktörlerini birleştirerek skor hesaplar.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IHumanAgentRegistry` | Aktif agent listesi ve yük bilgileri |
| `IOptions<RoutingOptions>` | Ağırlık ve eşik konfigürasyonu |

---

## `Decide`

```csharp
RoutingDecision Decide(
    ReasoningTrace trace,
    string? agentName,
    CustomerProfile? customerProfile)
```

### Akış

```
1. RoutingOptions.Enabled == false? → RoutingDecision { Note: "devre dışı" }

2. ExtractRequiredSkills(trace, agentName, customerProfile) → requiredSkills

3. IHumanAgentRegistry.GetActive()
   .Where(a => a.CurrentLoad < a.MaxConcurrentLoad)    ← kapasite dolu olanları ele

4. candidates.Count == 0? → RoutingDecision { MissingSkills: requiredSkills, "Müsait temsilci yok" }

5. Her aday için skor hesapla:
   skillMatch  = matchedSkills / max(requiredSkills, 1)
   langMatch   = agent dili müşteri diline uyuyor mu? 1 : 0
   loadFactor  = 1 - (currentLoad / maxLoad)  [LoadBalancingEnabled ise]
   priorityBoost = agent.Priority * 0.05  (max 0.2)

   score = (1 - LanguageWeight) * skillMatch
         + LanguageWeight * langMatch
         + priorityBoost
         + loadFactor * 0.05  [LoadBalancingEnabled ise, tie-break]

6. bestScore < MinMatchScore? → RoutingDecision { "Yeterli skill match yok" }

7. RoutingDecision { SuggestedAgentId, MatchScore, MatchedSkills, MissingSkills, Note }
```

---

## `ExtractRequiredSkills`

```csharp
List<string> ExtractRequiredSkills(
    ReasoningTrace trace,
    string? agentName,
    CustomerProfile? customerProfile)
```

4 kaynaktan skill tag'leri toplar:

**1. Intent → IntentSkillMap**  
`RoutingOptions.IntentSkillMap["order_inquiry"] = ["order", "logistics"]`

**2. Agent adı → tematik skill**

| Agent adı içeriyorsa | Eklenen skill |
|---------------------|--------------|
| "Complaint" | `complaint` |
| "Order" / "OrderPlacement" | `order` |
| "Product" | `product` |

**3. Admin notu → ProfileKeywordSkillMap**  
`RoutingOptions.ProfileKeywordSkillMap["VIP"] = "vip"`

**4. Müşteri dili**  
`profile.PreferredLanguage` (örn. "tr") doğrudan skill olarak eklenir.

Tüm skill'ler normalize edilir: küçük harf, trim.

---

## Skor formülü

```
skillMatch  = matchedSkills / max(requiredSkills.Count, 1)    [0, 1]
langMatch   = agent.Languages.Contains(preferredLang) ? 1 : 0  [0, 1]
loadFactor  = 1 - (currentLoad / maxConcurrentLoad)            [0, 1]
priorityBoost = min(agent.Priority * 0.05, 0.2)

score = (1 - LanguageWeight) * skillMatch
      + LanguageWeight * langMatch
      + priorityBoost
      [+ loadFactor * 0.05 if LoadBalancingEnabled]
```

---

## RoutingOptions konfigürasyonu

```json
{
  "Routing": {
    "Enabled": true,
    "LanguageWeight": 0.3,
    "MinMatchScore": 0.3,
    "LoadBalancingEnabled": true,
    "IntentSkillMap": {
      "order_inquiry": ["order", "logistics"],
      "complaint": ["complaint", "dispute-resolution"]
    },
    "ProfileKeywordSkillMap": {
      "VIP": "vip",
      "premium": "vip"
    }
  }
}
```

| Ayar | Açıklama |
|------|---------|
| `LanguageWeight` | Dil uyumu skorun ne kadarını etkiler (0.0–1.0) |
| `MinMatchScore` | Bu değerin altındaki skorlar "match yok" sayılır |
| `LoadBalancingEnabled` | Yük faktörünü tie-break'e dahil et |
| `IntentSkillMap` | Intent → skill listesi eşlemesi |
| `ProfileKeywordSkillMap` | Admin notu anahtar kelime → skill eşlemesi |

---

## RoutingDecision modeli

```csharp
public class RoutingDecision
{
    string? SuggestedAgentId;     // null: eşleşen yok
    string? SuggestedAgentName;
    double MatchScore;             // 0.0–1.0
    List<string> MatchedSkills;
    List<string> MissingSkills;
    string? Note;                  // Açıklama mesajı
}
```

---

## EscalationPolicyService ile entegrasyon

`EscalationPolicyService.ProcessPendingEscalations` her eskalasyon için `SkillsBasedRouter.Decide` çağırır. `MatchScore < MinMatchScore` ise eskalasyonun önceliği `High` olarak işaretlenir.
