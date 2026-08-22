# ChatResponse

**Dosya:** `Ports/Inbound/ChatResponse.cs`
**Tür:** `record`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

`IChatPort.HandleAsync`'in (non-streaming chat) döndürdüğü çıktı DTO'su — botun metin yanıtını, oturum kimliğini ve varsa reasoning (akıl yürütme) sonucunu taşır.

## 2. Hangi amaçla kullanılır?

Api katmanındaki non-streaming chat endpoint'i, bu tipi doğrudan JSON olarak HTTP response body'sine serileştirir.

## 3. Sorumlulukları

- **Üstlendiği:** Bir chat turunun tamamlanmış sonucunu (yanıt metni + oturum kimliği + isteğe bağlı reasoning trace) taşımak.
- **Üstlenmediği:** Yanıtın üretilme sürecini yönetmek — bu iş `ChatPortService`, `WorkflowRunner` gibi servislerdedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- `IChatPort`'un implementasyonu (`ChatPortService`) tarafından oluşturulur.
- Api katmanının chat endpoint'i tarafından tüketilir ve JSON'a çevrilir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Streaming (`HandleStreamAsync`/`StreamEvent`) ve non-streaming (`HandleAsync`/`ChatResponse`) yollar ayrı DTO'lara sahiptir çünkü ihtiyaçları farklıdır: streaming taraf artımlı event'ler yayar, non-streaming taraf turun sonunda tek bir tam sonuç döner.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Response` | `string` | Botun kullanıcıya gösterilecek nihai metni. |
| `SessionId` | `string` | Bu turun ait olduğu oturum kimliği (yeni oturumda API tarafından üretilmiş olabilir). |
| `Reasoning` | `ReasoningResult?` | Varsa, turun akıl yürütme (intent/plan) sonucu — debug/trace amaçlı. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.ReasoningResult` tipine bağımlı (Domain katmanı).

## Bağlantılar

- [IChatPort](IChatPort.md)
- [ChatRequest](ChatRequest.md)
