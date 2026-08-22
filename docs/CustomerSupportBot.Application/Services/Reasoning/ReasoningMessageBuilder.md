# ReasoningMessageBuilder

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/ReasoningMessageBuilder.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`ReasoningMessageBuilder`, Application/Services/ReasoningMessageBuilder.cs ReasoningService LLM çağrısı için system prompt + history + user query mesaj listesi kurar. <summary> Reasoning LLM'ine gönderilecek mesajları (system prompt + history + user query) kurar. Verified entity bloğunu, history note'unu ve session state'ini prompt template'e enjekte eder. </summary> <summary> Reasoning çağrısı için tam mesaj listesini kurar. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ReasoningMessageBuilder`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ReasoningMessageBuilder(IPromptRepository prompts)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Build`
```csharp
public List<ConversationMessage> Build(
        int maxHistoryMessages,
        string query,
        AgentSession session,
        List<ConversationMessage>? history,
        VerifiedEntities verified)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
