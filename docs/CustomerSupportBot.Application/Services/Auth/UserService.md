# UserService

- **Kaynak:** `CustomerSupportBot.Application/Services/Auth/UserService.cs`
- **Tür:** `public sealed class : IUserService`
- **Namespace:** `CustomerSupportBot.Application.Services.Auth`

## Ne işe yarar?

`UserService`, Application katmanında ilgili iş akışını ve domain kurallarını yürüten temel bileşendir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`UserService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public UserService(IUserAuthRepository users,
        IPasswordHasher hasher,
        ILogger<UserService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `AuthenticateAsync`
```csharp
public async Task<UserInfo?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IUserService`
