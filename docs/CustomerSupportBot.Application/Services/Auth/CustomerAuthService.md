# CustomerAuthService

- **Kaynak:** `CustomerSupportBot.Application/Services/Auth/CustomerAuthService.cs`
- **Tür:** `public sealed class : ICustomerAuthService`
- **Namespace:** `CustomerSupportBot.Application.Services.Auth`

## Ne işe yarar?

`CustomerAuthService`, Application/Services/Auth/CustomerAuthService.cs ICustomerAuthService driving port implementasyonu — müşteri self-servis kayıt/login. Staff (Admin/Agent) auth'unun (UserService/TokenPortService) aynı users tablosunu ve JWT boru hattını kullanır — Role="Customer" ile ayrışır, ayrı bir tablo/servis zinciri açmaz.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`CustomerAuthService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerAuthService(IUserAuthRepository users,
        ICustomerRepository customers,
        IPasswordHasher hasher,
        ILogger<CustomerAuthService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `RegisterAsync`
```csharp
public async Task<(UserInfo? User, string? Error)> RegisterAsync(
        string email, string password, string customerId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `AuthenticateAsync`
```csharp
public async Task<UserInfo?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ICustomerAuthService`
