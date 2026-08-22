# IReasoningPort

**Dosya:** `Ports/Inbound/IReasoningPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Kullanıcı mesajını, asıl workflow'a (uzman ajanlara) yönlendirmeden önce ön-analiz eden (niyet tespiti, plan çıkarma) reasoning aşamasının primary port'u.

## 2. Hangi amaçla kullanılır?

`ChatPortService`, bir kullanıcı mesajı geldiğinde önce bu portu çağırarak niyeti/planı çıkarır, sonra bu sonuca göre workflow'u (uzman ajanları) tetikler.

## 3. Sorumlulukları

- **Üstlendiği:** Sorgunun niyetini/planını çıkarmak, hem tek seferlik hem streaming biçimde.
- **Üstlenmediği:** Uzman ajanların asıl işi yapması — bu `WorkflowRunner`/uzman ajanların işidir; reasoning yalnızca ön-analizdir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Reasoning` altında; `ReasoningResultParser`/`PlanningResultParser` gibi Domain servisleriyle LLM çıktısını ayrıştırır.
- `ChatPortService` tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Reasoning'in ayrı bir adım/port olması, "önce ne yapılacağına karar ver, sonra yap" ilkesine hizmet eder — bu ayrım hem trace edilebilirliği (kullanıcıya/geliştiriciye "bot bu turda ne düşündü" gösterilebilir) hem de test edilebilirliği artırır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ReasoningResult> ReasonAsync(string query, AgentSession session, List<ConversationMessage>? history = null, CancellationToken ct = default)` | Non-streaming reasoning — tek `ReasoningResult` döndürür. |
| `IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string query, AgentSession session, List<ConversationMessage>? history = null, CancellationToken ct = default)` | Streaming reasoning — delta event'leri yayar. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model` (`AgentSession`, `ConversationMessage`, `ReasoningResult`).

## Bağlantılar

- [StreamEvent](StreamEvent.md), [IChatPort](IChatPort.md)
