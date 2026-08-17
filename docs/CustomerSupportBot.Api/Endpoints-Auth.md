# Endpoints — Auth

**Dosya:** `Endpoints/AuthEndpoints.cs`

Authentication endpoint'leri — JWT access + refresh token.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/auth/login` | POST | Anonymous | Username/password → JWT + refresh |
| `/auth/refresh` | POST | Anonymous | Refresh token → yeni JWT çifti |
| `/auth/logout` | POST | Authenticated | Refresh token revoke |

---

## `POST /auth/login`

```http
POST /auth/login
Content-Type: application/json

{ "username": "admin", "password": "..." }
```

**Akış:**

```
LoginRequest
   ↓ IUserService.AuthenticateAsync(username, password)
   - EfUserAuthRepository.FindActiveByUsernameAsync
   - BCryptPasswordHasher.Verify
   - UserInfo döner (yoksa null)
   ↓ ITokenService.IssueAsync(user)
   - JwtAccessTokenProvider.GenerateAccessToken
   - 64-byte CSPRNG refresh token
   - SHA-256(refresh) DB'ye yazılır
   ↓ Response
```

**200 Response:**

```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "abc-xyz-...",
  "accessTokenExpiry": "2026-05-24T11:00:00Z",
  "user": {
    "id": "u1",
    "username": "admin",
    "role": "Admin",
    "linkedAgentId": null
  }
}
```

**401 Response (yanlış kimlik):**

```json
{
  "status": 401,
  "title": "Authentication failed",
  "detail": "Invalid username or password"
}
```

**Önemli:** Username/password yanlışlığı **ayırt edilmez** — username enumeration saldırısı önlenir.

---

## `POST /auth/refresh`

```http
POST /auth/refresh
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

**Akış:**

```
RefreshRequest
   ↓ ITokenService.RefreshAsync(refreshToken)
   - SHA-256(refreshToken) hesapla
   - EfRefreshTokenRepository.FindByHashAsync
   - Kontroller: null değil, revoke edilmemiş, expire olmamış
   - EfUserAuthRepository.FindByIdAsync(userId)
   - Eski token revoke + ReplacedByTokenHash set
   - Yeni token çifti üret
   ↓ Response
```

**200 Response:**

```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "new-xyz-...",
  "accessTokenExpiry": "2026-05-24T11:00:00Z"
}
```

**401 Response (geçersiz/expired refresh):**

```json
{
  "status": 401,
  "title": "Refresh failed",
  "detail": "Refresh token is invalid, expired, or revoked"
}
```

### Rotation chain

```
t1 (revoke, replacedBy=t2)
  ↓
t2 (revoke, replacedBy=t3)
  ↓
t3 (active)
```

Bir client t1'i tekrar kullanırsa → muhtemelen çalıntı → tüm zincir revoke edilir. Detay: [Adapters.Persistence AuthAdapters](../CustomerSupportBot.Adapters.Persistence/AuthAdapters.md).

---

## `POST /auth/logout`

```http
POST /auth/logout
Authorization: Bearer <access-token>
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

**Akış:**

```
LogoutRequest
   ↓ ITokenService.RevokeAsync(refreshToken)
   - SHA-256(refreshToken) hesapla
   - EfRefreshTokenRepository.RevokeAsync(id, revokedAt)
   ↓ 204 No Content
```

**Access token'a ne olur?**

JWT stateless — server-side revoke edilemez. **Süresi dolana kadar geçerli kalır.** Mitigation:

- Access token kısa ömürlü tutulur (60 dakika default)
- Frontend logout sonrası token'ı localStorage'tan silmeli
- Kritik işlemler için JWT'nin yanında refresh check yapılabilir (bu projede yok)

Production için **token blacklist** (Redis) eklenebilir, ama performance trade-off var.

---

## JWT claim'leri

`JwtAccessTokenProvider.GenerateAccessToken` şu claim'leri ekler:

| Claim | Değer | Kullanım |
|---|---|---|
| `sub` | User ID | Authenticated user ID |
| `unique_name` | Username | Display |
| `name` | Username | Standard claim |
| `role` | "Admin", "Agent", "User" | Policy check |
| `linked_agent_id` | HumanAgent ID veya yok | Agent panel — load tracking |
| `iat` | Issue time | — |
| `exp` | Expiry | Validation |

### Authorization header kullanımı

Diğer endpoint'lerde:

```http
GET /admin/approvals/pending
Authorization: Bearer eyJhbGc...
```

ASP.NET Core JWT middleware token'ı parse eder, `HttpContext.User` claim'lerle dolar.

### Endpoint içinde claim erişimi

```csharp
adminGroup.MapGet("/profile", (HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst("sub")?.Value;
    var linkedAgentId = ctx.User.FindFirst("linked_agent_id")?.Value;
    // ...
});
```

---

## JWT validation parametreleri

```csharp
new TokenValidationParameters
{
    ValidateIssuer = true,                          // jwt.Issuer eşleşmeli
    ValidateAudience = true,                        // jwt.Audience eşleşmeli
    ValidateLifetime = true,                        // exp kontrolü
    ValidateIssuerSigningKey = true,                // imza kontrolü
    ValidIssuer = jwt.Issuer,
    ValidAudience = jwt.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(...),
    ClockSkew = TimeSpan.FromSeconds(30)            // 30s tolerans (server saatleri farkı)
}
```

`ClockSkew = 30s` — production'da clock drift için. Default 5 dakika, bu projede daha sıkı.

---

## Query string token (SSE / WebSocket)

EventSource ve WebSocket header gönderemez. `AuthServicesExtensions`'da:

```csharp
opts.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var token = context.Request.Query["access_token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(token))
            context.Token = token;
        return Task.CompletedTask;
    }
};
```

### Kullanım

```javascript
const es = new EventSource('/chat/events/sess-123?access_token=' + accessToken);
```

ASP.NET Core query string'den token okur, header gibi davranır.

**Güvenlik:**
- HTTPS şart (URL otomatik şifrelenir)
- Access token kısa ömürlü (60 dakika)
- Log filter'ı production'da `?access_token=` mask etmeli

---

## Bağlantılar

- [Application Auth (TokenPortService, UserService)](../CustomerSupportBot.Application/Auth/TokenPortService.md)
- [Adapters.Persistence AuthAdapters](../CustomerSupportBot.Adapters.Persistence/AuthAdapters.md) — BCrypt, JWT, refresh table
- [Models.md](Models.md) — `AuthDtos.cs`
- [Extensions.md](Extensions.md) — `AddAuthenticationServices`
