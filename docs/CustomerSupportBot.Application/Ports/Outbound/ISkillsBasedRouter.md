# ISkillsBasedRouter

**Kaynak:** `Ports/Outbound/ISkillsBasedRouter.cs`
**Implementasyon:** [`SkillsBasedRouter`](../../Services/Routing/SkillsBasedRouter.md)

## 1. Ne İşe Yarar

Eskalasyon için en uygun insan müşteri temsilcisini öneren router. Reasoning trace + opsiyonel
müşteri profili üzerinden skill gereksinimlerini çıkarır, [`IHumanAgentRegistry`](Persistence/IHumanAgentRegistry.md)'deki
adaylar arasında skor hesaplar ve en iyi eşleşmeyi döner.

## 2. Hangi Amaçla Kullanılır

Bir konuşma insan müdahalesi gerektirdiğinde (eskalasyon), eskalasyon kaydı oluşturulmadan
önce/sonra `Decide` çağrılarak hangi temsilciye önerileceği belirlenir.

## 3. Sorumlulukları

- **Üstlendiği:** Skill eşleştirme mantığı ve skorlama.
- **Üstlenmediği:** Eskalasyon kaydının kendisi — o [`IEscalationSink`](Persistence/IEscalationSink.md)'in işi; bu router yalnızca öneri üretir, atamayı zorlamaz.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Routing/SkillsBasedRouter` implemente eder;
[`IHumanAgentRegistry.GetActive()`](Persistence/IHumanAgentRegistry.md)'i okur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hiç temsilci yoksa veya skor eşik altında kalırsa `SuggestedAgentId` `null` döner — eskalasyon
yine de kaydedilir, admin manuel atayabilir. Bu tasarım, router'ın "kesin bir eşleşme yoksa
sessizce hiçbir öneri sunmama" ilkesini yansıtır; zayıf bir eşleşmeyi dayatmak yanlış
temsilciye yük bindirebilir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `RoutingDecision Decide(ReasoningTrace trace, string? agentName, CustomerProfile? customerProfile)` | En uygun temsilciyi önerir; eşleşme yoksa `SuggestedAgentId = null`. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ReasoningTrace` ve
`CustomerSupportBot.Domain.Model.Memory.CustomerProfile`'a bağımlıdır.
