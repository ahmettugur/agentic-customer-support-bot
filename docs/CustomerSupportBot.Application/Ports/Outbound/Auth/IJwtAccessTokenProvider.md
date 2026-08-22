# IJwtAccessTokenProvider

**Kaynak:** `Ports/Outbound/Auth/IJwtAccessTokenProvider.cs`
**Implementasyon:** [`JwtAccessTokenProvider`](../../../../CustomerSupportBot.Adapters.Persistence/Auth/JwtAccessTokenProvider.md)

## 1. Ne İşe Yarar

JWT access token üretimi için secondary port. `GenerateAccessToken(UserInfo, DateTime, int?)`
imzalı bir token ve son geçerlilik zamanını döner.

## 2. Hangi Amaçla Kullanılır

Login (admin/agent/müşteri), refresh-token yenileme ve A2A özne token'ı üretimi bu port
üzerinden çalışır.

## 3. Sorumlulukları

- **Üstlendiği:** `UserInfo`'dan JWT claim'lerini (rol, kullanıcı id, `LinkedCustomerId` vb.)
  çıkarıp imzalı bir token üretmek.
- **Üstlenmediği:** Refresh token yönetimi (bkz. [`IRefreshTokenRepository`](IRefreshTokenRepository.md)),
  şifre doğrulama (bkz. [`IPasswordHasher`](IPasswordHasher.md)).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Auth/JwtAccessTokenProvider` implemente eder; `System.IdentityModel.Tokens.Jwt`
kullanır. `JwtOptions`'tan (bkz. [JwtOptions](JwtOptions.md)) `SigningKey`, `Issuer`,
`Audience`, `AccessTokenMinutes` okur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`lifetimeMinutes` parametresi opsiyoneldir; `null` ise yapılandırmadaki varsayılan
(`AccessTokenMinutes`) kullanılır, mevcut çağıranların davranışı korunur. A2A özne token'ları
gibi tek bir çağrı için üretilen, dış sisteme verilen token'lar bilinçli olarak çok daha kısa
bir ömürle istenir — bu yüzden parametre override edilebilir bırakılmıştır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `(string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc, int? lifetimeMinutes = null)` | Erişim token'ı üretir. `lifetimeMinutes` verilmezse `JwtOptions.AccessTokenMinutes` kullanılır. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Auth.UserInfo`'ya bağımlıdır.
