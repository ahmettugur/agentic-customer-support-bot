# JwtOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Auth/JwtOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Auth`

## Ne işe yarar?

`JwtOptions`, Ports/Driven/Auth/JwtOptions.cs\n// JWT altyapı yapılandırması — Application katmanında tanımlıdır çünkü\n// hem Adapters.Persistence (TokenService) hem de API (AuthServicesExtensions) tarafından kullanılır.\n\nnamespace CustomerSupportBot.Application.Ports.Outbound.Auth; <summary>HMAC-SHA256 signing key (UTF-8). Üretimde rotate edilmeli.</summary> <summary>Access token ömrü (dakika).</summary> <summary>Refresh token ömrü (gün).</summary> <summary> /auth/* uçlarının (login, customer/login, customer/register, refresh) IP başına dakikalık hız sınırı. Bu uçlar kimliksizdir (AllowAnonymous) — A2A'daki gibi partner claim'i yoktur, tek ayırt edici çağıranın IP'sidir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`JwtOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Issuer` (`string`): İlgili veriyi temsil eden özellik.
- `Audience` (`string`): İlgili veriyi temsil eden özellik.
- `SigningKey` (`string`): İlgili veriyi temsil eden özellik.
- `AccessTokenMinutes` (`int`): İlgili veriyi temsil eden özellik.
- `RefreshTokenDays` (`int`): İlgili veriyi temsil eden özellik.
- `AuthRateLimitPerMinute` (`int`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
