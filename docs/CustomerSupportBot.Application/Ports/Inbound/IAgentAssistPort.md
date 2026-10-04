# IAgentAssistPort

**Dosya:** `Ports/Inbound/IAgentAssistPort.cs`
**Tür:** `interface` (+ sonuç kayıtları)
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Temsilci asistanının primary port'u: devralınan sohbet için özet, müşteri talebi, yanıt taslağı ve bağlam.

## 2. Hangi amaçla kullanılır?

`GET /chat-sessions/{sid}/assist` (admin) ve `GET /agent/chat-sessions/{sid}/assist` (agent) uçları çağırır.

## 3. Sorumlulukları

Sözleşme: oturum yoksa `null`; taslak yalnızca öneridir, gönderilmez.

## 4. Diğer katman/bileşenlerle ilişkileri

Uygulaması [`AgentAssistService`](../../Services/Escalation/AgentAssistService.md). Web tarafında aynı
şekilli kayıtlar `CustomerSupportBot.Web.Models` içindedir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

LLM'li alanlar (`Summary`, `CustomerRequest`, `SuggestedReply`) üretilemezse `AssistError` dolar; diğer
alanlar her zaman döner.

## 6. Metotlar / Üyeler

| Tip | Alanlar |
|---|---|
| `AgentAssistResult` | `SessionId`, `Summary?`, `CustomerRequest?`, `SuggestedReply?`, `AssistError?`, `Sentiment`, `Profile?`, `Articles`, `OpenItems` |
| `AgentAssistSentiment` | `Label?`, `Score`, `ConsecutiveNegativeTurns` |
| `AgentAssistProfile` | `CustomerId`, `Summary?`, `PreferredTone`, `ProductInterests`, `TopIntents`, `AverageRating?`, `TotalSessions` |
| `AgentAssistArticle` | `Title`, `Snippet`, `Source?`, `Score` |
| `AgentAssistOpenItem` | `Kind` (`escalation`/`approval`), `Id`, `Description`, `Status`, `CreatedAt` |

## 7. Bağımlılıklar

Yok.
