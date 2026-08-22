# Routing

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Routing/Routing.cs`
- **Tür:** `internal static class` & `Strategy Pattern`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Routing`

## Ne işe yarar?

`Routing.cs`, [CustomerSupportChatManager](CustomerSupportChatManager.md) tarafından kullanılan Strategy Pattern tabanlı çoklu ajan yönlendirme mimarisidir. İlk tur yönlendirmesi (`FirstTurnStrategy`), planlama sonrası yönlendirme (`PlanRoutingStrategy`) ve uzman ReAct çıktısı sonrası yönlendirme (`ReflectionRoutingStrategy`) stratejilerini barındırır.

## Hangi amaçla kullanılır`?

İş akışı sırasında hangi ajanın çalışacağına deterministik kurallarla karar vermek; `RoutingContext` üzerinden paylaşılan ajan sözlüğünü ve kısıtlanmış uzman (`ConstrainedSpecialist`) kurallarını yönetmek için kullanılır.

## Yönlendirme Stratejileri

| Strateji | Açıklama |
|---|---|
| `FirstTurnStrategy` | İlk turda doğrudan `PlanningAgent`'ı (veya kısıtlanmış uzmanı) seçer. |
| `PlanRoutingStrategy` | `PlanningAgent` tarafından üretilen `PlanningResult` çıktısını parse ederek netleştirme gerekirse `ResponseAgent`'a, aksi halde seçilen uzmana yönlendirir. |
| `ReflectionRoutingStrategy` | Uzman ajanın `postToolReflection` çıktısını inceleyerek görev tamamsa `ResponseAgent`'a, eskalasyon gerekiyorsa `HumanHandoffAgent`'a yönlendirir. |

## Bağımlılıklar

- `Microsoft.Agents.AI.AIAgent`
- `CustomerSupportBot.Domain.Model.WorkflowGuardOptions`
- [PlanningResult](../CustomerSupportBot.Domain/Model/PlanningResult.md)
