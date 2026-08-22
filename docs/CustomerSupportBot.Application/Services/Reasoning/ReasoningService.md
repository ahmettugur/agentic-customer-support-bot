# ReasoningService

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/ReasoningService.cs`
- **Tür:** `public  class : IReasoningPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`ReasoningService`, Application/Services/ReasoningService.cs Kullanıcı sorgusu için açık (explicit) reasoning adımları üretir. <summary> Reasoning adımlarını üretir ve yapılandırılmış formatta döner. Group chat workflow'dan bağımsız çalışır — önce reasoning, sonra ana workflow. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ReasoningService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ReasoningService(IReasoningChatClient reasoningClient,
        ILogger<ReasoningService> logger,
        IPromptRepository prompts,
        EntityVerifier entityVerifier,
        ReasoningSanityChecker sanityChecker,
        IOptions<WorkflowGuardOptions> guards)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ReasonAsync`
```csharp
public async Task<ReasoningResult> ReasonAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ReasonStreamingAsync`
```csharp
public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        [EnumeratorCancellation] CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IReasoningPort`
