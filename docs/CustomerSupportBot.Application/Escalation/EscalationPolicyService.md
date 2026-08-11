# EscalationPolicyService

**Dosya:** `Services/Escalation/EscalationPolicyService.cs`

## 1. Ne İşe Yarar

Eskalasyon oluşturulduğunda skills-based routing uygular, EscalationRequest'in RequiredSkills, Priority ve SuggestedAgent alanlarını doldurur.

## 2. Hangi Amaçla Kullanılır

PostToolReflection.Status = "needs_escalation" olduğunda bu policy servisi çağrılır. SkillsBasedRouter'a delege eder.

## Bağlantılar

- [../Routing/SkillsBasedRouter.md](../Routing/SkillsBasedRouter.md) — Routing algoritması
- [HumanAgentPortService.md](HumanAgentPortService.md) — Temsilci yönetimi
