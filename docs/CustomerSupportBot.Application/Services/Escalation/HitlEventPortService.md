# HitlEventPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Escalation/HitlEventPortService.cs`
- **Tür:** `public sealed class : IHitlEventPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## Ne işe yarar?

`HitlEventPortService`, Application/Services/HitlEventPortService.cs DRIVING PORT IMPL — IHitlEventPort → approval/escalation event aboneliği.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`HitlEventPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public HitlEventPortService(IApprovalQueue approvals,
        IEscalationSink escalations,
        IChatModeRegistry modeRegistry,
        ILogger<HitlEventPortService>? logger = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Subscribe`
```csharp
public IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SubscribeToChatEvents`
```csharp
public IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Dispose`
```csharp
public void Dispose()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Dispose`
```csharp
public void Dispose()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IHitlEventPort`
