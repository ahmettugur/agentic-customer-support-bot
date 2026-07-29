# Auth Adaptörleri

**Dosyalar:**  
- `Auth/BCryptPasswordHasher.cs`  
- `Auth/JwtAccessTokenProvider.cs`  
- `Auth/TokenService.cs` (dokümantasyon notu)  
- `EfCore/Auth/EfUserAuthRepository.cs`  
- `EfCore/Auth/EfRefreshTokenRepository.cs`  

---

## BCryptPasswordHasher

**Dosya:** `Auth/BCryptPasswordHasher.cs`  
**Port:** `IPasswordHasher`

BCrypt.NET kütüphanesini kullanır. Work factor: **11** (güvenlik/performans dengesi).

```csharp
public string Hash(string password)
    => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

public bool Verify(string password, string hash)
    => BCrypt.Net.BCrypt.Verify(password, hash);
```

BCrypt'in kasıtlı yavaşlığı brute-force saldırılarını engeller. Work factor 11 ~100ms/hash yapar.

---

## JwtAccessTokenProvider

**Dosya:** `Auth/JwtAccessTokenProvider.cs`  
**Port:** `IJwtAccessTokenProvider`

JWT access token üretir. `JwtOptions` yapılandırmasından signing key ve ayarları okur.

### `GenerateAccessToken`

```csharp
(string token, DateTime expiry) GenerateAccessToken(UserInfo user, DateTime issuedAt)
```

**Token claim'leri:**

| Claim | Değer |
|-------|-------|
| `sub` | `user.Id` |
| `unique_name` | `user.Username` |
| `name` | `user.Username` |
| `role` | `user.Role` |
| `linked_agent_id` | `user.LinkedAgentId` (varsa) |
| `jti` | `JwtRegisteredClaimNames.Jti` — her token için benzersiz Guid |
| `iat` | Issue time |
| `exp` | `issuedAt + AccessTokenMinutes` |

**Güvenlik gereksinimleri:**
- Signing key minimum **32 karakter** olmalı (HMAC-SHA256)
- Daha kısa key ile uygulama başlamaz (startup validation)

### JwtOptions yapılandırması

```json
{
  "Jwt": {
    "SigningKey": "minimum-32-karakter-gizli-anahtar!!!",
    "Issuer": "CustomerSupportBot",
    "Audience": "CustomerSupportBot",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 14
  }
}
```

> Config key **`Jwt:SigningKey`**'dir, `Jwt:Secret` değil. `JwtOptions` sınıfının kod içi varsayılanları `AccessTokenMinutes = 30`, `RefreshTokenDays = 14`'tür — yukarıdaki JSON `appsettings.json`'daki gerçek değerlerdir (varsayılanları override eder).

---

## TokenService.cs

Bu dosya bir implementasyon değil, bir açıklama notudur. Token orchestration `Application.Services.Auth.TokenPortService`'e taşınmıştır. `JwtAccessTokenProvider` yalnızca JWT imzalama görevini üstlenir.

---

## EfUserAuthRepository

**Dosya:** `EfCore/Auth/EfUserAuthRepository.cs`  
**Port:** `IUserAuthRepository`  
**Bağımlılık:** `IDbContextFactory<CustomerSupportDbContext>` (Singleton'dan Scoped DbContext üretir)

| Metod | SQL | Açıklama |
|-------|-----|---------|
| `FindActiveByUsernameAsync` | `WHERE username = @u AND is_active = true` | Login için kullanıcı bul |
| `FindByIdAsync` | `WHERE id = @id` | Refresh token yenilemede kullanıcı doğrula |
| `UpdateLastLoginAsync` | `UPDATE auth.users SET last_login_at = @now` | Her başarılı girişte güncelle |

**Entity → UserInfo dönüşümü:**

```csharp
new UserInfo(
    entity.Id,
    entity.Username,
    entity.PasswordHash,
    entity.Role,
    entity.LinkedAgentId,
    entity.IsActive
)
```

---

## EfRefreshTokenRepository

**Dosya:** `EfCore/Auth/EfRefreshTokenRepository.cs`  
**Port:** `IRefreshTokenRepository`  
**Bağımlılık:** `IDbContextFactory<CustomerSupportDbContext>`

| Metod | Açıklama |
|-------|---------|
| `CreateAsync(id, userId, hash, expiry, now)` | Yeni refresh token kaydı |
| `FindByHashAsync(hash)` | Hash ile bul — plain text **asla** DB'ye yazılmaz |
| `RevokeAsync(id, revokedAt, replacedByHash)` | Revoke et, rotation zinciri için `ReplacedByTokenHash` yaz |

**Rotation zinciri:** Her `RefreshAsync` çağrısında eski token revoke edilir, yeni token `replacedByTokenHash` ile bağlanır. Token çalınma tespiti için zincir izlenebilir.

---

## Auth akışı özeti

```
Login
  UserService.AuthenticateAsync(username, password)
    ↓
  EfUserAuthRepository.FindActiveByUsernameAsync()
  BCryptPasswordHasher.Verify(password, hash)
    ↓
  TokenPortService.IssueAsync(userInfo)
    ↓
  EfRefreshTokenRepository.CreateAsync(hash)   ← plain text DB'ye gitmez
  JwtAccessTokenProvider.GenerateAccessToken() ← JWT imzala
    ↓
  AuthResponse { accessToken, refreshToken, ... }

Refresh
  TokenPortService.RefreshAsync(refreshToken)
    ↓
  HashToken(refreshToken) → SHA-256
  EfRefreshTokenRepository.FindByHashAsync(hash)
    → geçerlilik kontrolleri (null / revoked / expired)
  EfRefreshTokenRepository.RevokeAsync(old)    ← token rotation
  EfRefreshTokenRepository.CreateAsync(new)
  JwtAccessTokenProvider.GenerateAccessToken()
    ↓
  AuthResponse yeni çift

Logout
  TokenPortService.RevokeAsync(refreshToken)
    ↓
  EfRefreshTokenRepository.RevokeAsync(id)
```
