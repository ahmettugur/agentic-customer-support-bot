# CustomerUnderstandingService

- **Kaynak:** `CustomerSupportBot.Application/Services/Personalization/CustomerUnderstandingService.cs`
- **Tür:** `public sealed class : ICustomerUnderstandingService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## Ne işe yarar?

`CustomerUnderstandingService`, Application/Services/Personalization/CustomerUnderstandingService.cs CustomerProfile'ı ICustomerUnderstandingService port'u üzerinden sentezler. LLM ÇAĞIRMAZ — saf, senkron bir dönüşümdür; maliyet sınıfı RecordInteractionAsync'le aynı.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`CustomerUnderstandingService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerUnderstandingService(ICustomerProfileStore store)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Build`
```csharp
public CustomerUnderstanding? Build(AgentSession session)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ICustomerUnderstandingService`
