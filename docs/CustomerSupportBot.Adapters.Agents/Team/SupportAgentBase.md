# SupportAgentBase

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/SupportAgentBase.cs`
- **Tür:** `internal abstract class : DelegatingAIAgent`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`SupportAgentBase`, Microsoft Agents Framework (MAF) içerisindeki `ChatClientAgent` sınıfı `sealed` olduğu için doğrudan kalıtım almak yerine `DelegatingAIAgent` sınıfından türeyen; takımdaki tüm uzman ajanların ortak temel sınıfıdır.

## Hangi amaçla kullanılır`?

MAF iş akışı ajanları `TurnToken(emitEvents: true)` ile her zaman `RunCoreStreamingAsync` üzerinden çağrıldığından; hem senkron (`RunCoreAsync`) hem de asenkron akış (`RunCoreStreamingAsync`) çağrılarında debug noktaları (`OnBeforeRun`, `OnAfterRun`) sunmak ve araç çağrılarını (`ToolCalls`, `ToolResults`) kolayca ayıklamak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `DelegatingAIAgent` ile iç ajana delegasyon yapmak.
  - `RunCoreAsync` ve `RunCoreStreamingAsync` çağrılarında `OnBeforeRun` ve `OnAfterRun` hook'larını çalıştırmak.
  - Yanıttan araç çağrılarını (`FunctionCallContent`) ve sonuçlarını (`FunctionResultContent`) ayıklamak.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `OnBeforeRun` | Metot | `protected abstract void OnBeforeRun(IReadOnlyList<ChatMessage> messages)` | LLM'e gönderilmek üzere olan mesaj listesini yakalar (Breakpoint noktası). |
| `OnAfterRun` | Metot | `protected abstract void OnAfterRun(AgentResponse response)` | LLM'in bu ajan için ürettiği tam yanıtı yakalar (Breakpoint noktası). |
| `ToolCalls` | Metot | `protected static List<FunctionCallContent> ToolCalls(AgentResponse response)` | Yanıttaki fonksiyon çağrılarını filtreler. |
| `ToolResults` | Metot | `protected static List<FunctionResultContent> ToolResults(AgentResponse response)` | Yanıttaki fonksiyon sonuçlarını filtreler. |

## Bağımlılıklar

- `Microsoft.Agents.AI.DelegatingAIAgent`
- `Microsoft.Extensions.AI.ChatMessage`
