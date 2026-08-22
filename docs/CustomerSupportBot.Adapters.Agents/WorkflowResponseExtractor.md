# WorkflowResponseExtractor

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowResponseExtractor.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowResponseExtractor`, Microsoft Agents Framework (MAF) iş akışı çıktı olaylarından (`WorkflowOutputEvent`) ve ara mesaj koleksiyonlarından (`IEnumerable<ChatMessage>`) anlamlı sonuç metinlerini, planlama modellerini (`PlanningResult`), uzman ReAct JSON'larını (`SpecialistReasoning`) ve sonlanma/yönlendirme sinyallerini çıkaran yardımcı sınıftır.

## Hangi amaçla kullanılır`?

Uzman ajanların ürettiği `SpecialistReasoningSchema` JSON çıktılarını ve `ResponseAgent`'ın `TERMINATE` işaretçilerini ayrıştırmak, ReDoS saldırılarına karşı korumalı regex zaman aşımı (`RegexTimeout = 500ms`) kullanmak ve anormal sonlanma durumlarında bile ara durumlardan plan/eskalasyon verisi kurtarmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `ExtractResultFromOutput` ile nihai asistan yanıtını çıkarmak.
  - `ExtractPlanningFromOutput` ile `PlanningResult` nesnesini ayrıştırmak.
  - `ExtractSpecialistReasonings` ile uzman ajanların `preToolCheck` ve `postToolReflection` bloklarını toplamak.
  - `ExtractLastDraft` ile `ResponseAgent`'ın ilk taslak yanıtını ve öz-eleştiri JSON'unu ayıklamak.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `ExtractResultFromOutput` | Metot | `public static string ExtractResultFromOutput(WorkflowOutputEvent output)` | İş akışından kullanıcıya gösterilecek nihai metni çıkarır. |
| `ExtractPlanningFromOutput` | Metot | `public static PlanningResult? ExtractPlanningFromOutput(WorkflowOutputEvent output)` | Çıktıdan planlama sonucunu ayrıştırır. |
| `ExtractSpecialistReasonings` | Metot | `public static List<SpecialistReasoning> ExtractSpecialistReasonings(IEnumerable<ChatMessage> chatMessages)` | Mesajlardan uzman ReAct akıl yürütmelerini toplar. |
| `StripTerminationMarker` | Metot | `public static string StripTerminationMarker(string text)` | Metinden `TERMINATE: reason=...` işaretini ve sonrasındaki JSON bloğunu temizler. |

## Bağımlılıklar

- `Microsoft.Agents.AI.Workflows.WorkflowOutputEvent`
- [PlanningResult](../CustomerSupportBot.Domain/Model/PlanningResult.md)
- [SpecialistReasoning](../CustomerSupportBot.Domain/Model/SpecialistReasoning.md)
