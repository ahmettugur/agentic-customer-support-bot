# JwtAccessTokenProvider

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Auth/JwtAccessTokenProvider.cs`
- **Port:** `IJwtAccessTokenProvider` (`CustomerSupportBot.Application.Ports.Outbound.Auth`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Auth`

## 1. Ne İşe Yarar

JWT (JSON Web Token) erişim token'ı üreten adaptördür. `System.IdentityModel.Tokens.Jwt` kütüphanesini kullanarak `UserInfo`'dan imzalı bir HMAC-SHA256 JWT oluşturur.

## 2. Hangi Amaçla Kullanıldığı

Login/refresh akışlarının (staff ve müşteri, `TokenPortService`) token üretim adımıdır — kullanıcı kimliği doğrulandıktan sonra istemciye dönecek `access_token`'ı burada üretilir.

## 3. Sorumlulukları

- Constructor'da `JwtOptions.SigningKey`'in en az 32 karakter olduğunu doğrulama (HMAC-SHA256 için minimum anahtar uzunluğu) — kısa/boş anahtarla uygulama **başlangıçta** hata verir, ilk login denemesinde değil (fail-fast).
- `GenerateAccessToken` — standart claim'leri (`sub`, `unique_name`, `NameIdentifier`, `Name`, `Role`, `jti`) ve role-özel bağ claim'lerini (`linked_agent_id` staff için, `linked_customer_id` müşteri için — ikisi de sadece doluysa eklenir) içeren imzalı token üretir.

**Üstlenmediği:** Refresh token üretimi/rotasyonu (bkz. `TokenPortService`, Application katmanı — refresh token ayrı bir mekanizma, opak rastgele string + veritabanı kaydı).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `TokenPortService` (Application katmanı) tarafından çağrılır — orkestrasyon orada, imzalama burada; bu ayrım `TokenService.cs`'in (bkz. [TokenService.md](TokenService.md)) artık bir stub olmasının doğrudan sonucudur.
- `linked_customer_id` claim'i, `ApprovalGateService`'in (Adapters.Agents) `CurrentCustomerId`'yi JWT'den okumasının temelidir — müşteri artık tool parametresi olarak `customerId` göndermez.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

IdentityModel bağımlılığının bu adaptöre izole edilmesi — Application/Domain katmanları JWT kütüphanesini hiç bilmez, sadece `IJwtAccessTokenProvider` portunu görür (Dependency Inversion). `linked_agent_id`/`linked_customer_id`'nin **koşullu** eklenmesi (`if (!string.IsNullOrWhiteSpace(...))`) — token boyutunu gereksiz büyütmemek ve bir staff kullanıcının token'ında asla `linked_customer_id` görünmemesini (ve tersini) garanti eder.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `JwtAccessTokenProvider(IOptions<JwtOptions>)` | `SigningKey` uzunluk kontrolü yapar, geçersizse `InvalidOperationException`. |
| `GenerateAccessToken(user, nowUtc, lifetimeMinutes)` | İmzalı JWT ve son kullanma zamanını `(string Token, DateTime ExpiresAt)` olarak döner; `lifetimeMinutes` verilmezse `JwtOptions.AccessTokenMinutes` kullanılır. |

## 7. Bağımlılıklar

- `IOptions<JwtOptions>`
