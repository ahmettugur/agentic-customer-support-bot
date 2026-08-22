# TokenPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Auth/TokenPortService.cs`
- **Tür:** `public sealed class : ITokenService`
- **Namespace:** `CustomerSupportBot.Application.Services.Auth`

## Ne işe yarar?

`TokenPortService`, Application/Services/Auth/TokenPortService.cs ITokenService driving port implementasyonu. Refresh token lifecycle orkestrasyonu Application core'da; JWT imzalama IJwtAccessTokenProvider driven port'u üzerinden.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`TokenPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public TokenPortService(IUserAuthRepository users,
        IRefreshTokenRepository tokens,
        IJwtAccessTokenProvider jwt,
        IOptions<JwtOptions> options,
        ICustomerRepository? customers = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `IssueAsync`
```csharp
public async Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `RefreshAsync`
```csharp
public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `RevokeAsync`
```csharp
public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ITokenService`
