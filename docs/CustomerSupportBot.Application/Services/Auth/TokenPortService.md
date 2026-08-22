# TokenPortService

**Dosya:** `Services/Auth/TokenPortService.cs`
**Port:** `ITokenService` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Auth`

## 1. Ne İşe Yarar

JWT access token + refresh token yaşam döngüsünü (üretim, yenileme/rotasyon, iptal) yönetir.
Access token'ı imzalama işini `IJwtAccessTokenProvider`'a (Adapters katmanı) devreder; kendisi
refresh token'ın kalıcılığı ve **rotasyon güvenliğiyle** ilgilenir.

## 2. Hangi Amaçla Kullanılır

Bir kullanıcı (staff veya müşteri) login olduğunda [`UserService`](UserService.md)/[`CustomerAuthService`](CustomerAuthService.md)
kimlik doğrulamasını yapar, sonucu bu servise (`IssueAsync`) verir; bu servis JWT çiftini
üretir. Access token süresi dolduğunda istemci `RefreshAsync`'i çağırır. Logout'ta `RevokeAsync`
çağrılır.

## 3. Sorumlulukları

- **Üstlendiği:** Refresh token üretimi (kriptografik rastgele, hash'lenmiş halde saklama),
  rotasyon (her yenilemede eski token iptal edilip yenisi verilir), **atomik/koşullu iptal**
  (bkz. §5), giriş adının/rolünün/tam adının `AuthResponse`'a yansıtılması.
- **Üstlenmediği:** JWT'nin imzalanması/claim yapısı (`IJwtAccessTokenProvider`), kullanıcının
  kim olduğunun doğrulanması (bu, `IssueAsync`'i çağıran taraf — `UserService`/`CustomerAuthService` —
  sorumluluğundadır; bu servis zaten doğrulanmış bir `UserInfo` alır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ITokenService` port'unu implemente eder.
- **Inject eder:** `IUserAuthRepository`, `IRefreshTokenRepository`, `IJwtAccessTokenProvider`,
  `IOptions<JwtOptions>`, `ICustomerRepository?` (opsiyonel — müşteri tam adını çözmek için).
- **Kimin tarafından çağrılır:** Api katmanındaki `/auth/*` ve `/auth/customer/*` endpoint'leri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`TryRevokeAsync` ile koşullu sahiplenme — refresh token rotasyonunun atomikleştirilmesi.**
> `RefreshAsync` önce eski token'ı okur, sonra yeni bir token üretip eskisini iptal edip
> yenisini kaydeder. Bu üç adım arasında bir **yarış durumu** vardı: aynı refresh token'la
> (ör. çalınmış/paylaşılmış bir token'la) eşzamanlı gelen iki yenileme isteği, ikisi de "token
> hâlâ geçerli" okumasını yapıp ikisi de kendi yeni token'ını üretebilirdi — tek bir token'dan
> iki ayrı, birbirinden habersiz oturum zinciri doğardı. Düzeltme: iptal artık
> `IRefreshTokenRepository.TryRevokeAsync(id, revokedAt, replacedByTokenHash, ct)` ile **tek bir
> koşullu `UPDATE ... WHERE Id = @id AND RevokedAt IS NULL`** olarak yapılır (EF Core
> `ExecuteUpdateAsync`). Yalnızca token GERÇEKTEN hâlâ iptal edilmemişse iptal başarılı sayılır;
> eşzamanlı ikinci istek `false` alır ve `RefreshAsync` `null` döner — o istemci yeniden login
> olmak zorunda kalır. Bu, "aynı zamanda okundu, ikisi de geçerli gördü" senaryosunu güvenli hale
> getirir.

Refresh token **düz metin olarak asla saklanmaz** — `GenerateRefreshToken`, 64 baytlık
kriptografik rastgele bir değer üretir, istemciye düz metni (`Plain`) döner ama veritabanına
SHA-256 hash'ini (`Hash`) yazar. Bir veritabanı sızıntısında token'ların kendisi ele geçirilemez.

`ResolveFullNameAsync`, müşteri hesapları için görüntülenecek adı **`UserInfo.LinkedCustomerId`**
üzerinden çözer — yani JWT'nin zaten bağlı olduğu hesaptan, istekten gelen hiçbir parametreden
değil. Bu, başka bir müşterinin adının bu yolla yanlışlıkla dönmesini yapısal olarak imkânsız
kılar. Repo çözülemezse (`_customers is null`, staff hesabı, veya kayıt yok) sessizce `null`
döner — ad sadece gösterim amaçlıdır, eksikliği login'i bozmamalıdır.

`RevokeAsync` (logout) `TryRevokeAsync` DEĞİL, düz `RevokeAsync` kullanır çünkü burada rotasyon
yarışı yoktur — tek bir istemcinin kendi token'ını iptal etmesi, herhangi bir "hâlâ geçerli mi"
koşuluna bağlı değildir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `IssueAsync(UserInfo user, CancellationToken ct = default): Task<AuthResponse>` | Yeni refresh token üretir, kaydeder, `LastLoginAt`'i günceller, access token'ı imzalar. |
| `RefreshAsync(string refreshToken, CancellationToken ct = default): Task<AuthResponse?>` | Refresh token'ı doğrular, koşullu olarak iptal edip rotasyonlar; yarışta kaybederse veya token geçersizse `null`. |
| `RevokeAsync(string refreshToken, CancellationToken ct = default): Task<bool>` | Logout — token'ı koşulsuz iptal eder. |
| `GenerateRefreshToken()` *(private static)* | 64 baytlık kriptografik rastgele token + SHA-256 hash'i üretir. |
| `HashToken(string token)` *(private static)* | SHA-256 hash, hex string. |
| `ResolveFullNameAsync(UserInfo user, CancellationToken ct)` *(private)* | Müşteri hesapları için katalogdaki tam adı `LinkedCustomerId` üzerinden çözer. |

## 7. Bağımlılıklar (Constructor Injection)

- `IUserAuthRepository` — kullanıcı kaydı (`FindByIdAsync`, `UpdateLastLoginAsync`).
- `IRefreshTokenRepository` — refresh token kalıcılığı (`CreateAsync`, `FindByHashAsync`, `TryRevokeAsync`, `RevokeAsync`).
- `IJwtAccessTokenProvider` — access token imzalama (Adapters katmanı).
- `IOptions<JwtOptions>` — `RefreshTokenDays` gibi ayarlar.
- `ICustomerRepository?` (opsiyonel) — müşteri tam adı çözümü.

## Bağlantılar

- [CustomerAuthService.md](CustomerAuthService.md) — müşteri kayıt/login (bu servisi çağıran taraflardan biri)
- [UserService.md](UserService.md) — staff login (bu servisi çağıran diğer taraf)
