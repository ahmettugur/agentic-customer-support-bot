# IChatPort

**Dosya:** `Ports/Inbound/IChatPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Chat kullanım senaryosunun (bir kullanıcı mesajını işleyip bot yanıtı üretmek) primary/driving port'u. Sistemin en merkezi arayüzü — kullanıcı-bot etkileşiminin girişi budur.

## 2. Hangi amaçla kullanılır?

Api katmanındaki HTTP adaptörü (chat endpoint'leri) bu arayüze bağımlıdır, somut implementasyona (`ChatPortService`) değil — hexagonal mimarinin temel prensibi budur.

## 3. Sorumlulukları

- **Üstlendiği:** Bir chat turunu iki modda sunmak: tek seferde tam yanıt (non-streaming) veya artımlı event akışı (SSE streaming).
- **Üstlenmediği:** Turun içindeki reasoning/routing/tool-çağırma detayları — bunlar implementasyonun (`ChatPortService`, `WorkflowRunner`) içindedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu: `ChatPortService` (Application/Services/Chat).
- Api katmanındaki chat endpoint'leri (`ChatAndRealtime` grubu) bu portu kullanır.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

İki ayrı metodun (streaming/non-streaming) var olmasının nedeni farklı istemci ihtiyaçlarıdır: web arayüzü kullanıcıya token-token akan bir yanıt göstermek ister (SSE), programatik/test entegrasyonları ise tek bir tam yanıt bekler.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default)` | Non-streaming chat: sorguyu işler ve tek bir JSON yanıt döner. |
| `IAsyncEnumerable<StreamEvent> HandleStreamAsync(ChatRequest request, CancellationToken ct = default)` | SSE streaming chat: reasoning → workflow → yanıt deltalarını olay akışı olarak yayar. |

## 7. Bağımlılıklar

[ChatRequest](ChatRequest.md), [ChatResponse](ChatResponse.md), [StreamEvent](StreamEvent.md).

## Bağlantılar

- [ChatRequest](ChatRequest.md), [ChatResponse](ChatResponse.md), [StreamEvent](StreamEvent.md)
