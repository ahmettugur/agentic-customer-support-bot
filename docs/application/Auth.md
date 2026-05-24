# Auth Servisleri

**Dosyalar:**  
- `Services/Auth/TokenPortService.cs` — JWT + refresh token yönetimi  
- `Services/Auth/UserService.cs` — kullanıcı kimlik doğrulama  

## Genel Bakış

Auth katmanı iki servis içerir. `UserService` kullanıcı adı/şifre doğrular; `TokenPortService` access + refresh token çiftini üretir ve döngüsel yenileme (rotation) uygular.

```
API (Login endpoint)
    │
    ├── UserService.AuthenticateAsync(username, password)
    │       → UserInfo veya null
    │
    └── TokenPortService.IssueAsync(userInfo)
            → AuthResponse (accessToken, refreshToken, expiry, ...)
```

---

## TokenPortService

**Implements:** `ITokenService`  
**Yaşam döngüsü:** Singleton

### Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IUserAuthRepository` | Kullanıcı bilgileri ve son giriş güncelleme |
| `IRefreshTokenRepository` | Refresh token CRUD |
| `IJwtAccessTokenProvider` | JWT imzalama (Adapters.Auth) |
| `IOptions<JwtOptions>` | Token süreleri ve yapılandırma |

---

### `IssueAsync`

```csharp
Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default)
```

Yeni bir access + refresh token çifti üretir.

**Akış:**
```
1. GenerateRefreshToken()
   → 64 byte kriptografik rastgele veri
   → Base64 plain text (kullanıcıya döner)
   → SHA-256 hash (DB'ye kaydedilir, plain text hiç yazılmaz)

2. IRefreshTokenRepository.CreateAsync(tokenId, userId, hash, expiry)

3. IUserAuthRepository.UpdateLastLoginAsync(userId, now)

4. IJwtAccessTokenProvider.GenerateAccessToken(user, now)
   → (accessToken string, accessExpiry)

5. AuthResponse döner
```

---

### `RefreshAsync`

```csharp
Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
```

Refresh token rotasyonu uygular — eski token geçersiz kılınır, yeni çift üretilir.

**Akış:**
```
1. HashToken(refreshToken)
2. IRefreshTokenRepository.FindByHashAsync(hash)
3. Geçerlilik kontrolü:
   - null → null dön
   - RevokedAt != null → null dön (revoke edilmiş)
   - ExpiresAt <= now → null dön (süresi dolmuş)
4. IUserAuthRepository.FindByIdAsync(userId)
   - null veya !IsActive → null dön
5. Yeni refresh token üret
6. Eski tokenı revoke et: RevokeAsync(id, now, newHash)
7. Yeni tokenı kaydet: CreateAsync(...)
8. Yeni JWT üret ve AuthResponse döner
```

**Rotation:** Her `RefreshAsync` çağrısında eski token geçersiz kılınır. Çalınmış token ikinci kez kullanılamaz.

---

### `RevokeAsync`

```csharp
Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
```

Logout işlemi. Hash ile token'ı bulur ve revoke eder.

---

### Token güvenliği

Refresh token'lar veritabanında **sadece SHA-256 hash** olarak saklanır. Plain text hiçbir zaman DB'ye yazılmaz; sadece HTTP yanıtta döner.

```csharp
private static (string Plain, string Hash) GenerateRefreshToken()
{
    Span<byte> bytes = stackalloc byte[64];
    RandomNumberGenerator.Fill(bytes);       // kriptografik rastgele
    var plain = Convert.ToBase64String(bytes);
    return (plain, HashToken(plain));        // SHA-256
}
```

---

## UserService

**Implements:** `IUserService`  
**Yaşam döngüsü:** Singleton

### Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IUserAuthRepository` | Kullanıcı sorgulama |
| `IPasswordHasher` | Bcrypt/Argon2 şifre doğrulama (Adapters.Auth) |

---

### `AuthenticateAsync`

```csharp
Task<UserInfo?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
```

**Akış:**
```
1. username / password boş kontrolü → null dön

2. IUserAuthRepository.FindActiveByUsernameAsync(username)
   → kullanıcı bulunamazsa veya pasifse → null dön + Warning log

3. IPasswordHasher.Verify(password, user.PasswordHash)
   → eşleşmiyorsa → null dön + Warning log

4. UserInfo döner
```

Başarısız denemeler Warning seviyesinde loglanır (username loglanır, şifre loglanmaz).

---

## JwtOptions konfigürasyonu

```json
{
  "Jwt": {
    "Secret": "...",
    "Issuer": "CustomerSupportBot",
    "Audience": "CustomerSupportBotUsers",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 30
  }
}
```

---

## AuthResponse modeli

```csharp
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessExpiry,
    DateTime RefreshExpiry,
    string Username,
    string Role,
    string? LinkedAgentId
);
```

---

## API endpoint'leri

```http
POST /auth/login    → UserService.AuthenticateAsync + TokenPortService.IssueAsync
POST /auth/refresh  → TokenPortService.RefreshAsync
POST /auth/logout   → TokenPortService.RevokeAsync
```
