# AuthServicesExtensions

- **Dosya:** `Extensions/AuthServicesExtensions.cs`
- **Namespace:** `CustomerSupportBot.Api.Extensions`

## 1. Ne İşe Yarar

JWT tabanlı kimlik doğrulama/yetkilendirme altyapısını (`AddAuthentication().AddJwtBearer(...)`)
ve uygulama genelindeki tüm authorization policy'lerini (`Admin`, `Agent`, `Customer`, `Partner`,
`A2ASubject`, `SessionAccess`, `AdminOrAgent`) tek noktada kaydeden Composition Root extension'ı.

## 2. Hangi Amaçla Kullanılır

`Program.cs` açılışta `builder.Services.AddAuthenticationServices(configuration)` çağırır. Bu
çağrı olmadan hiçbir `[RequireAuthorization(...)]`/`RequireAuthorization("Policy")` çağrısı
çalışmaz — DI konteynerinde ilgili şema/politika tanımlı olmaz.

## 3. Sorumlulukları

- `JwtOptions`'ı `appsettings.json`'dan bağlar (`Configure<JwtOptions>`).
- `IPasswordHasher` (BCrypt), `IJwtAccessTokenProvider`, `ITokenService`, `IUserService`
  (staff), `ICustomerAuthService` (müşteri) implementasyonlarını DI'a kaydeder.
- JWT doğrulama parametrelerini (`Issuer`, `Audience`, `SigningKey`, `ClockSkew`) kurar.
- SSE/`EventSource` bağlantıları `Authorization` header'ı gönderemediği için `OnMessageReceived`
  event'inde `?access_token=` query string'ini token kaynağı olarak kabul eder.
- Yedi authorization policy'sini tanımlar (bkz. bölüm 6).
- **Üstlenmediği:** token'ın gerçek üretimi/imzalanması (`JwtAccessTokenProvider`'ın işi),
  kullanıcı doğrulama mantığı (`UserService`/`CustomerAuthService`'in işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IUserService`, `ICustomerAuthService`, `ITokenService`, `IJwtAccessTokenProvider`,
  `IPasswordHasher` — `CustomerSupportBot.Application.Ports.{Inbound,Outbound}.Auth`.
- `JwtOptions` — `Application.Ports.Outbound.Auth`.
- `A2ARoles` — `Application.Services.A2A`; `Partner`/`A2ASubject` politikaları bu rol adlarını
  kullanır.
- `Program.cs` — çağıran; ayrıca `app.UseAuthentication()`/`app.UseAuthorization()`
  middleware'lerini (bu dosyada değil, `Program.cs`'te) doğru sırada devreye sokar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`SessionAccess` politikası çıplak `RequireAuthorization()` yerine açık rol listesi
  (`Customer`, `Admin`, `Agent`) kullanır:** A2A token'ları da aynı JWT şemasıyla doğrulanır;
  "kimliği doğrulanmış olmak yeterli" kuralı kullanılsaydı bir partner token'ı da `/sessions/*`
  uçlarına girip, `linked_customer_id` claim'i yokluğunda "sınırsız" (admin gibi) kapsam
  kazanabilirdi.
- **A2A için iki ayrı rol (`Partner`/`A2ASubject`):** partner token'ı müşteri-bağımsız işlemler
  (ürün sorgulama, token değişimi) için, özne token'ı tek bir müşteriye kilitli veri erişimi
  için — birbirinin yerine geçemez.
- **`RequireHttpsMetadata = false` yorumla açıkça "Development için" işaretli:** production'da
  HTTPS zorunluluğunun kaldırılmadığından emin olmak için okuyanın dikkatini çeker.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddAuthenticationServices(this IServiceCollection, IConfiguration)` | JWT bearer authentication'ı ve tüm authorization policy'lerini kaydeder. |

**Tanımlanan policy'ler:** `Admin`, `Agent`, `AdminOrAgent`, `Customer`, `SessionAccess`
(`Customer`+`Admin`+`Agent`), `Partner` (A2A), `A2ASubject` (A2A).

## 7. Bağımlılıklar

Extension metodu olduğundan constructor injection yok; `IServiceCollection`/`IConfiguration`
parametre olarak alınır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../Endpoints/A2A.md](../Endpoints/A2A.md)
- [../Endpoints/Intelligence.md](../Endpoints/Intelligence.md)
