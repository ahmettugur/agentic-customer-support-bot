# ReplanService

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/ReplanService.cs`
- **Tür:** `public sealed class : IReplanService`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`ReplanService`, Application/Services/ReplanService.cs IReplanService implementasyonu: bir session'daki son kullanıcı mesajını yeniden değerlendirerek bot yanıtı üretir ve bridge aracılığıyla müşteriye iletir. <summary> Replan use case implementasyonu. Reasoning + workflow pipeline'ı koşturur, yanıtı session history'ye yazar ve ChatBridge üzerinden müşteriye bot mesajı olarak yayınlar. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ReplanService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ReplanService(ISessionManager sessions,
        IChatBridge bridge,
        IAgentTeamPort team,
        IReasoningPort reasoning,
        IApprovalContextAccessor approvalContext,
        ILogger<ReplanService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ExecuteAsync`
```csharp
public async Task ExecuteAsync(string sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IReplanService`
