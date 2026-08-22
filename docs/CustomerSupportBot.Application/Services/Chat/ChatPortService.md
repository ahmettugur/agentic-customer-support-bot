# ChatPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/ChatPortService.cs`
- **Tür:** `public sealed class : IChatPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`ChatPortService`, <summary> IChatPort implementasyonu — chat kullanım senaryosunu orkestre eder. Human mode (HITL), sentiment güncelleme ve persist işlemleri burada yönetilir. </summary> <summary> Bir turun kilidi bekleyebileceği azami süre. Bir tur LLM çağrıları yüzünden onlarca saniye sürebilir; ikinci mesaj REDDEDİLMEK yerine SIRAYA girmelidir, çünkü amaç

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ChatPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ChatPortService(IAgentTeamPort team,
        IReasoningPort reasoning,
        ISessionManager sessions,
        IChatModeRegistry modeRepo,
        IChatBridge chatBridge,
        SessionStateService sessionState,
        IApprovalContextAccessor approvalContext,
        IAppDistributedLock turnLock,
        ILogger<ChatPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `HandleAsync`
```csharp
public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `HandleStreamAsync`
```csharp
public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IChatPort`
