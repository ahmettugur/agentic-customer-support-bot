# PersonalizationPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Personalization/PersonalizationPortService.cs`
- **Tür:** `public sealed class : IPersonalizationPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## Ne işe yarar?

`PersonalizationPortService`, Application/Services/PersonalizationPortService.cs DRIVING PORT IMPL — IPersonalizationPort → CustomerProfileService + ICustomerProfileStore.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`PersonalizationPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public PersonalizationPortService(CustomerProfileService profileService, ICustomerProfileStore profiles)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetProfile`
```csharp
public CustomerProfile? GetProfile(string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `RefreshProfileAsync`
```csharp
public Task<CustomerProfile?> RefreshProfileAsync(string customerId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SetAdminNote`
```csharp
public CustomerProfile SetAdminNote(string customerId, string? note)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteProfile`
```csharp
public bool DeleteProfile(string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IPersonalizationPort`
