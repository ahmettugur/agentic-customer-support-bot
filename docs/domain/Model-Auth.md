# Auth Modelleri

**Dosyalar:**
- `Model/Auth/UserInfo.cs`
- `Model/Auth/RefreshTokenInfo.cs`

Auth altyapı detayları (BCrypt, JWT, DB) **adapter**'larda. Domain sadece **kimlik ve token yapısı**nı tanımlar.

---

## UserInfo

```csharp
public sealed record UserInfo(
    string Id,
    string Username,
    string PasswordHash,
    string Role,
    string? LinkedAgentId,
    bool IsActive
);
```

| Alan | Açıklama |
|---|---|
| `Id` | Primary key (`Guid` string'i) |
| `Username` | Login için |
| `PasswordHash` | BCrypt hash — **plain text asla saklanmaz** |
| `Role` | `"Admin"`, `"Agent"`, `"User"` |
| `LinkedAgentId` | Eğer kullanıcı bir `HumanAgent`'a bağlıysa, agent'ın ID'si |
| `IsActive` | `false` ise login engellenir |

### LinkedAgentId nedir?

Bazı kullanıcılar (Role=`Agent`) live-takeover'da **fiziksel insanlar**. Her insanın hem bir `User` kaydı (auth için) hem bir `HumanAgent` kaydı (skill, load tracking için) vardır. `LinkedAgentId` bu ikisini bağlar:

```
Users tablosu                  HumanAgents tablosu
─────────────                  ───────────────────
Id=u1                          Id=a1
Username=ali.demir             Skills=["complaint","tr"]
Role=Agent             ───┐    MaxLoad=5
LinkedAgentId=a1       ───┴─→  IsActive=true
```

Login sonrası JWT claim'lerinde `linked_agent_id=a1` taşınır; takeover sırasında bu agent'ın load'u artırılır.

---

## RefreshTokenInfo

```csharp
public sealed record RefreshTokenInfo(
    string Id,
    string UserId,
    string TokenHash,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    string? ReplacedByTokenHash
);
```

| Alan | Açıklama |
|---|---|
| `Id` | Token kaydı PK |
| `UserId` | Sahibi kullanıcı |
| `TokenHash` | **SHA-256(plain token)** — plain DB'ye gitmez |
| `ExpiresAt` | Son kullanma (default: 30 gün) |
| `RevokedAt` | Logout veya rotation'da set edilir |
| `ReplacedByTokenHash` | Token rotation zinciri — eski token hangi yeniyle değiştirildi |

### Token rotation neden?

Her refresh çağrısında eski token revoke edilir, yeni token yaratılır. Zincir izlenebilir:

```
t1 (revoked, replacedBy=t2)
  ↓
t2 (revoked, replacedBy=t3)
  ↓
t3 (active)
```

Eğer çalıntı bir token (t1) tekrar kullanılırsa, sistem zincirin tamamını revoke eder — çalınma tespiti. Bu mantık `TokenPortService`'te (Application katmanı), DB tarafı `EfRefreshTokenRepository`'de.

---

## Akış

```
Login
  → BCryptPasswordHasher.Verify(plainPassword, user.PasswordHash)
  → TokenPortService.IssueAsync(user) generates:
      - JWT access token (signed)
      - Random 64-byte refresh token (plain)
      - SHA-256(refresh) DB'ye yazılır

Refresh
  → Client gönderir: plain refresh token
  → SHA-256 hesapla, DB'de FindByHashAsync
  → Geçerliyse: eski'yi revoke, yeni token çifti üret

Logout
  → Refresh token revoke
```

Detaylar için [Adapters Auth dokümantasyonu](../adapters-persistence/AuthAdapters.md).

---

## Neden Domain'de saf record?

`UserInfo` ve `RefreshTokenInfo` saf data transfer record'lar:
- EF Core entity (`UserEntity`) → `Adapters.Persistence` katmanında
- BCrypt, JWT, SHA-256 → `Adapters.Persistence/Auth/` altında
- Domain sadece **şekil**i tanımlar

Bu sayede Domain'i değiştirmeden BCrypt yerine Argon2 kullanabilir, JWT yerine başka token şeması seçebilirsin.
