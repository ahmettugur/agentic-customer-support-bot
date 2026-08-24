# EfUserAuthRepository

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/Auth/EfUserAuthRepository.cs`
- **Tür:** `public sealed class : IUserAuthRepository`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore.Auth`

## Ne işe yarar?

`EfUserAuthRepository`, Application katmanındaki [IUserAuthRepository](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IUserAuthRepository.md) portunu uygulayan; `IDbContextFactory<CustomerSupportDbContext>` üzerinden kullanıcı bulma (`FindActiveByUsernameAsync`, `FindByIdAsync`), oluşturma (`CreateAsync`) ve son giriş zamanı güncelleme (`UpdateLastLoginAsync`) işlemlerini yöneten adaptördür.

## Hangi amaçla kullanılır`?

- JWT kimlik doğrulama akışında kullanıcı adı ve parola hash'lerini doğrulamak.
- Kullanıcıya bağlı `LinkedCustomerId` müşteri eşleştirmesini Domain modeline ([UserInfo](../../../CustomerSupportBot.Domain/Model/Auth/UserInfo.md)) taşımak.

## Sorumlulukları

- **Üstlendiği:**
  - `IUserAuthRepository` arayüzündeki tüm CRUD metotlarını karşılamak.
  - `UserEntity` ile `UserInfo` arasındaki eşlemeyi (`Map`) yapmak.

## Constructor ve Başlatma Mantığı

```csharp
public EfUserAuthRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
```

### Constructor İçerisinde Yapılan İşler:
- `_dbFactory` (`IDbContextFactory<CustomerSupportDbContext>`): Her işlemde kısa ömürlü, thread-safe DbContext üretmek üzere atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `FindActiveByUsernameAsync`
```csharp
public async Task<UserInfo?> FindActiveByUsernameAsync(string username, CancellationToken ct = default)
```
- **Ne işe yarar?:** Aktif kullanıcıyı kullanıcı adına göre getirir.
- **İç Mantığı:** `ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username && u.IsActive)` sorgusunu işletir ve `Map` ile döner.

### 2. `FindByIdAsync`
```csharp
public async Task<UserInfo?> FindByIdAsync(string id, CancellationToken ct = default)
```
- **Ne işe yarar?:** Kullanıcıyı Id'ye göre getirir (JWT claim'inden gelen kimliği doğrulamak/tazelemek için).

### 3. `CreateAsync`
```csharp
public async Task<UserInfo?> CreateAsync(
    string username, string passwordHash, string role, string? linkedCustomerId, CancellationToken ct = default)
```
- **Ne işe yarar?:** Yeni bir kullanıcı kaydı açar; kullanıcı adı zaten varsa `null` döner.

### 4. `UpdateLastLoginAsync`
- **Ne işe yarar?:** Başarılı girişte `LastLoginAt` alanını günceller.

## Bağımlılıklar

- [IUserAuthRepository](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IUserAuthRepository.md)
- [CustomerSupportDbContext](../CustomerSupportDbContext.md)
- [UserInfo](../../../CustomerSupportBot.Domain/Model/Auth/UserInfo.md)
