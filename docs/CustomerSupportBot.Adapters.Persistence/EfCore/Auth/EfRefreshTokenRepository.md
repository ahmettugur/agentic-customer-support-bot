# EfRefreshTokenRepository

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/Auth/EfRefreshTokenRepository.cs`
- **Tür:** `public sealed class : IRefreshTokenRepository`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore.Auth`

## Ne işe yarar?

`EfRefreshTokenRepository`, Application katmanındaki [IRefreshTokenRepository](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.md) portunu uygulayan; JWT yenileme token'larının (Refresh Token) kaydedilmesini (`SaveAsync`), doğrulanmasını (`FindByTokenHashAsync`), iptal edilmesini (`RevokeAsync`, `RevokeAllForUserAsync`) ve süresi dolmuş token'ların temizlenmesini (`DeleteExpiredAsync`) yöneten adaptördür.

## Hangi amaçla kullanılır`?

- Güvenli JWT oturum tazeleme (Token Rotation) mekanizmasını desteklemek.
- Token'ın kendisini değil, SHA-256 hash'ini (`TokenHash`) saklayarak veri tabanı sızıntılarına karşı güvenlik sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - `IRefreshTokenRepository` sözleşmesini karşılamak.
  - `RefreshTokenEntity` ile [RefreshTokenInfo](../../../CustomerSupportBot.Domain/Model/Auth/RefreshTokenInfo.md) arasında çift yönlü dönüşüm sağlamak.

## Constructor ve Başlatma Mantığı

```csharp
public EfRefreshTokenRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
```

### Constructor İçerisinde Yapılan İşler:
- `_dbFactory` (`IDbContextFactory<CustomerSupportDbContext>`): Asenkron kısa ömürlü DbContext üreticisi atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `FindByTokenHashAsync`
```csharp
public async Task<RefreshTokenInfo?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
```
- **Ne işe yarar?:** Token hash'ine göre aktif yenileme token'ını arar.

### 2. `SaveAsync`
```csharp
public async Task SaveAsync(RefreshTokenInfo tokenInfo, CancellationToken ct = default)
```
- **Ne işe yarar?:** Yeni bir yenileme token'ı kaydeder.

### 3. `RevokeAsync` & `RevokeAllForUserAsync`
- **Ne işe yarar?:** Token'ın `RevokedAt` ve `ReplacedByTokenHash` alanlarını doldurarak iptal eder.

## Bağımlılıklar

- [IRefreshTokenRepository](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.md)
- [CustomerSupportDbContext](../CustomerSupportDbContext.md)
- [RefreshTokenInfo](../../../CustomerSupportBot.Domain/Model/Auth/RefreshTokenInfo.md)
