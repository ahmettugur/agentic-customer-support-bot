# JwtOptions

**Kaynak:** `Ports/Outbound/Auth/JwtOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"Jwt"` (`JwtOptions.SectionName`)

## 1. Ne İşe Yarar

JWT altyapısının tüm konfigürasyonunu taşır: issuer/audience, imzalama anahtarı, token
ömürleri ve `/auth/*` uçları için hız sınırı.

## 2. Hangi Amaçla Kullanılır

Hem `Adapters.Persistence` (`JwtAccessTokenProvider`, `EfRefreshTokenRepository`) hem
`Api` (`AuthServicesExtensions`, `AuthEndpoints`) tarafından okunur — bu yüzden bu options
sınıfı, her iki katmanın da bağımlı olduğu Application katmanında tanımlıdır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca yapılandırma değerlerini taşımak.
- **Üstlenmediği:** Token üretimi/doğrulama mantığı — [`IJwtAccessTokenProvider`](IJwtAccessTokenProvider.md)'da.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`AuthServicesExtensions` (Api katmanı) JWT bearer authentication middleware'ini bu ayarlarla
konfigüre eder; `AuthEndpoints` `/auth` grubuna `AuthRateLimitPerMinute` ile rate limiting
uygular.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`AuthRateLimitPerMinute`'ün ayrı bir konfigürasyon alanı olmasının nedeni: `/auth/*` uçları
kimliksizdir (`AllowAnonymous`) — A2A'daki gibi bir partner claim'i yoktur, tek ayırt edici
çağıranın IP'sidir. Bu limit sabit kodlanmak yerine appsettings'ten okunabilir olmalıdır ki
test ortamında (çok sayıda ardışık login çağıran paylaşımlı test fixture'ları) veya
prod'da farklı değerlerle çalışılabilsin.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `string Issuer` | `"CustomerSupportBot.Api"` | JWT `iss` claim'i. |
| `string Audience` | `"CustomerSupportBot.Api"` | JWT `aud` claim'i. |
| `string SigningKey` | `""` | HMAC-SHA256 imzalama anahtarı (UTF-8). Üretimde rotate edilmelidir. |
| `int AccessTokenMinutes` | `30` | Access token ömrü. |
| `int RefreshTokenDays` | `14` | Refresh token ömrü. |
| `int AuthRateLimitPerMinute` | `10` | `/auth/*` uçlarının IP başına dakikalık hız sınırı. |

## 7. Bağımlılıklar

Yok — saf options sınıfı.
