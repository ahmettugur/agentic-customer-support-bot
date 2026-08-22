# IWorkflowRunner

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/IWorkflowRunner.cs`
- **Tür:** `internal interface`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`IWorkflowRunner`, tek bir alt görevi veya bağımsız kullanıcı sorgusunu koşturan [WorkflowRunner](WorkflowRunner.md) motorunun [DecomposedRunner](DecomposedRunner.md) tarafından kullanılan dar yüzeyini tanımlayan arayüzdür.

## Hangi amaçla kullanılır`?

`WorkflowRunner` sınıfının 6 ajanı ve MAF altyapısını kurmasını gerektiren ağır bağımlılıklarını izole ederek, `DecomposedRunner`'ın compound sorgu bölme ve paralel/sıralı orkestrasyon mantığının Docker/Testcontainers olmadan birim testlerde (Unit Tests) taklit edilebilmesini (mocking) sağlamak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - Tek alt görev için `RunAsync` ve `RunStreamingAsync` metot imzalarını sunmak.

## Diğer katman ve bileşenlerle ilişkileri

- **Namespace / Katman:** `CustomerSupportBot.Adapters.Agents`.
- **Uygulayan:** [WorkflowRunner](WorkflowRunner.md).
- **Kullanan:** [DecomposedRunner](DecomposedRunner.md).

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `RunAsync` | Metot | `Task<string> RunAsync(string query, List<ConversationMessage>? conversationHistory, AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)` | Tek alt görevi çalıştırıp yanıt metnini döner. |
| `RunStreamingAsync` | Metot | `IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query, List<ConversationMessage>? conversationHistory, AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)` | Tek alt görevi çalıştırıp olay akışını döner. |

## Bağımlılıklar

- [StreamEvent](../CustomerSupportBot.Application/Ports/Inbound/StreamEvent.md)
- [ReasoningResult](../CustomerSupportBot.Domain/Model/ReasoningResult.md)
- [AgentSession](../CustomerSupportBot.Domain/Model/AgentSession.md)
