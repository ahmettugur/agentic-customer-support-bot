# Routing

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Routing/Routing.cs`
- **Tür:** `internal static class` & `Strategy Pattern`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Routing`

## Ne işe yarar?

`Routing.cs`, [CustomerSupportChatManager](CustomerSupportChatManager.md) tarafından kullanılan Strategy Pattern tabanlı çoklu ajan yönlendirme mimarisidir. İlk tur yönlendirmesi (`FirstTurnStrategy`), planlama sonrası yönlendirme (`PlanRoutingStrategy`) ve uzman ReAct çıktısı sonrası yönlendirme (`ReflectionRoutingStrategy`) stratejilerini barındırır.

## Hangi amaçla kullanılır`?

İş akışı sırasında hangi ajanın çalışacağına deterministik kurallarla karar vermek; `RoutingContext` üzerinden paylaşılan ajan sözlüğünü ve kısıtlanmış uzman (`ConstrainedSpecialist`) kurallarını yönetmek için kullanılır.

> Not: Bu dosya, kaynak kodun tek bir dosyada (`Routing.cs`) topladığı, birbirinden bağımsız
> anlamı olmayan küçük tipleri (bir `record struct`, bir sabit sınıfı, bir context ve üç
> strateji) TEK bir dokümanda birleştirir — ayrı ayrı belgelemek her birinin diğerlerine
> sürekli referans vermesine yol açardı.

## İçerdiği Tipler

### `RoutingResult` (`internal readonly record struct`)
`(AIAgent Agent, string Branch)` — bir stratejinin ürettiği karar: hangi ajana geçilecek ve
hangi tanılama/telemetri dalından (`Branch`) geçildi.

### `Branches` (`internal static class`)
Her yönlendirme kararının hangi mantıksal daldan geldiğini isimlendiren `string` sabitleri
(`first_turn`, `plan_parse_failed`, `plan_clarification`, `plan_unknown_agent`, `plan`,
`plan_constrained`, `reflection_missing`, `reflection_escalation`, `reflection_handoff`,
`reflection_complete`, `reflection_constrained`). Trace/log'larda "neden bu ajana gidildi"
sorusunu cevaplamak için kullanılır — sihirli string yerine tek yerden yönetilir.

### `RoutingContext` (`internal sealed class`)
Üç stratejinin paylaştığı salt-okunur bağlam: `AgentsByName` (isimden ajana sözlük),
`PlanningAgent`, `ResponseAgent`, `Guards` (`WorkflowGuardOptions`), `Logger`,
`ConstrainedSpecialist` (decomposed/alt-görev akışında yönlendirmeyi tek bir uzmana kilitlemek
için, `null` ise kısıtlama yok).

| Üye | Açıklama |
|---|---|
| `Resolve(string? name)` | İsmi `AgentsByName`'de arar, yoksa/boşsa `null` döner. |
| `IsSpecialistMessage(ChatMessage msg)` (static) | Mesajın yazarı `WellKnown.AgentNames.Specialists` önekleriyle mi başlıyor kontrolü — mesaj bir uzman ajandan mı geldi sorusu. |
| `GetSpecialistName(ChatMessage msg)` (static) | Mesajın hangi uzman ön ekiyle eşleştiğini döner (loglama/dal seçimi için). |

### `IRoutingStrategy` (`internal interface`)
Tek metot: `ValueTask<RoutingResult?> TrySelectAsync(IReadOnlyList<ChatMessage> history, ChatMessage? lastMessage, CancellationToken ct)`.
Strateji bu turda karar veremiyorsa (kendi koşulu tutmuyorsa) `null` döner — `CustomerSupportChatManager`
sıradaki stratejiye geçer.

## Yönlendirme Stratejileri

| Strateji | `TrySelectAsync` mantığı |
|---|---|
| `FirstTurnStrategy` | Geçmişte `PlanningAgent` hiç konuşmadıysa `PlanningAgent`'ı (dalı `first_turn`) seçer; aksi halde `null` döner (ilk tur değil). |
| `PlanRoutingStrategy` | Son mesaj `PlanningAgent`'tan değilse `null`. Değilse `PlanningResultParser.TryParse` ile JSON'ı çözer: parse başarısızsa `ResponseAgent`/`plan_parse_failed`; `NeedsClarification` ise `ResponseAgent`/`plan_clarification`; önerilen ajan bulunamazsa `ResponseAgent`/`plan_unknown_agent`; `ConstrainedSpecialist` set edilmiş ve plan farklı bir uzmanı önerdiyse yine de `ConstrainedSpecialist`'e zorlanır (`plan_constrained`, uyarı loglanır); aksi halde önerilen uzmana gider (`plan`). |
| `ReflectionRoutingStrategy` | Son mesaj bir uzmandan değilse `null`. `SpecialistReasoningParser.TryParse` ile `postToolReflection`'ı çözer: yoksa `ResponseAgent`/`reflection_missing`; durum `NeedsEscalation` ise `ResponseAgent`/`reflection_escalation` (fiili eskalasyon kaydı `EscalationPolicyService`'te, burada sadece yönlendirme yapılır); bir `HandoffSuggestion` varsa ve hedef `ConstrainedSpecialist`'e aykırıysa engellenir (`reflection_constrained`); geçerliyse o ajana (`reflection_handoff`); yoksa `ResponseAgent`/`reflection_complete`. |

## Bağımlılıklar

- `Microsoft.Agents.AI.AIAgent`
- `CustomerSupportBot.Domain.Model.WorkflowGuardOptions`
- [PlanningResult](../CustomerSupportBot.Domain/Model/PlanningResult.md)
