# SkillsBasedRouter

- **Kaynak:** `CustomerSupportBot.Application/Services/Routing/SkillsBasedRouter.cs`
- **Tür:** `public  class : ISkillsBasedRouter`
- **Namespace:** `CustomerSupportBot.Application.Services.Routing`

## Ne işe yarar?

`SkillsBasedRouter`, Application/Services/Routing/SkillsBasedRouter.cs SkillsBasedRouter — Reasoning trace + müşteri profilinden skill etiketlerini çıkarır, IHumanAgentRegistry'deki aday temsilciler arasında en iyi skill + dil eşleşmesini bulur. LLM-siz, deterministik ve hızlı (<1ms).  Skor Formülü: skillMatch  = matchedSkills / max(requiredSkills, 1)            ∈ [0,1] langMatch   = profile-language ∈ agent.Languages ? 1 : 0        ∈ {0,1} loadFactor  = 1 - (currentLoad / maxLoad)                        ∈ [0,1] priorityBoost = agent.Priority * 0.05                            (cap +0.2) score = (1 - LanguageWeight) * skillMatch + LanguageWeight * langMatch tie-break: yüksek loadFactor + Priority + son atama eskiliği <summary> Eskalasyon için skill-based temsilci yönlendirmesi yapan servis. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SkillsBasedRouter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public SkillsBasedRouter(IHumanAgentRegistry registry, IOptions<RoutingOptions> options)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Decide`
```csharp
public RoutingDecision Decide(
        ReasoningTrace trace,
        string? agentName,
        CustomerProfile? customerProfile)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ExtractRequiredSkills`
```csharp
public List<string> ExtractRequiredSkills(
        ReasoningTrace trace,
        string? agentName,
        CustomerProfile? customerProfile)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ISkillsBasedRouter`
