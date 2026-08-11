# SkillsBasedRouter

**Dosya:** `Services/Routing/SkillsBasedRouter.cs`

## 1. Ne İşe Yarar

Eskalasyon talebinin `RequiredSkills` listesini aktif temsilcilerin `Skills` listesiyle eşleştirerek en uygun temsilciyi seçer. Match score hesaplar, capacity (MaxConcurrentLoad vs CurrentLoad) kontrol eder.

## 2. Neden Gerekli

> 💡 **Analiz notu:** Bir çağrı merkezinin ACD (Automatic Call Distributor) sistemi gibi — "Türkçe bilen, şikayet konusunda uzman, şu an boş olan temsilci kim?" sorusuna cevap verir.

## 3. Scoring Algoritması

```
matchedCount = intersection(requiredSkills, agentSkills)
totalRequired = requiredSkills.Count
baseScore = matchedCount / totalRequired
capacityPenalty = currentLoad / maxConcurrentLoad
finalScore = baseScore * (1 - capacityPenalty * 0.3)
```

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/HumanAgent.md](../../CustomerSupportBot.Domain/Model/HumanAgent.md) — Temsilci modeli
- [../Escalation/EscalationPolicyService.md](../Escalation/EscalationPolicyService.md) — Bu router'ı çağıran policy
