# IAgentTeamPort

**Kaynak:** `Ports/Outbound/IAgentTeamPort.cs`
**Implementasyon:** [`CustomerSupportTeam`](../../../CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.md)

## 1. Ne İşe Yarar

Müşteri destek ajan takımının (MAF workflow'unun) tamamını Application katmanına tek bir
sözleşme olarak sunan secondary port: non-streaming çalıştırma, streaming çalıştırma, workflow
diyagramı.

## 2. Hangi Amaçla Kullanılır

`ChatPortService` bir kullanıcı sorgusu geldiğinde `RunAsync` (bekleyen tam cevap) veya
`RunStreamingAsync` (SSE ile parça parça) çağırır. Admin/debug panelindeki workflow görselleştirme
`GetWorkflowDiagram`'ı kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Ajan takımının çalıştırılmasının Application katmanına sunulan tek giriş
  noktası olmak.
- **Üstlenmediği:** MAF workflow'unun iç işleyişi (ajanlar arası handoff, tool çağrıları,
  reasoning) — bunlar `Adapters.Agents` katmanında kalır, Application bunları bilmez.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Agents/CustomerSupportTeam` implemente eder — 6 uzman ajan + `GroupChatHost`'u
içeren gerçek MAF `Workflow`'unu sarar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu port, hexagonal mimarideki en önemli sınırlardan biridir: Application katmanı MAF'a
(Microsoft Agent Framework) DOĞRUDAN bağımlı değildir, yalnızca bu ince arayüze bağımlıdır —
ajan orkestrasyon teknolojisi değiştirilebilir olur.

`GetWorkflowDiagram` argüman almaz çünkü graph topolojisi tur/oturumdan bağımsız sabittir
(aynı 6 ajan + `GroupChatHost`); MAF'ın `Workflow.ToMermaidString()` extension'ını sarar,
dokümantasyon ve debug amaçlıdır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<string> RunAsync(string query, List<ConversationMessage>? conversationHistory = null, AgentSession? session = null, ReasoningResult? reasoning = null, CancellationToken ct = default)` | Sorguyu workflow'da koşturur, nihai yanıtı döner (non-streaming). |
| `IAsyncEnumerable<StreamEvent> RunStreamingAsync(...)` | Workflow'u SSE stream event'leri olarak koşturur (aynı parametreler). |
| `string GetWorkflowDiagram()` | Ajan takımı workflow graph'ının Mermaid.js diyagramını döner. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model` tiplerine (`ConversationMessage`, `AgentSession`,
`ReasoningResult`) ve `CustomerSupportBot.Application.Ports.Inbound.StreamEvent`'e bağımlıdır.
