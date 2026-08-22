# IUserService

**Dosya:** `Ports/Inbound/Auth/IUserService.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## 1. Ne işe yarar?

Staff (admin/agent) kullanıcılarının kimlik doğrulaması için primary port.

## 2. Hangi amaçla kullanılır?

Api katmanındaki `/auth/login` (staff girişi) endpoint'i, kullanıcı adı+şifreyi bu porta iletir; başarılıysa dönen `UserInfo` `ITokenService.IssueAsync`'e verilir.

## 3. Sorumlulukları

- **Üstlendiği:** Kullanıcı adı+şifre kombinasyonunun `UserEntity` tablosuna karşı doğrulanması.
- **Üstlenmediği:** Müşteri hesap doğrulaması — bu `ICustomerAuthService`'in işidir; iki hesap türü kasıtlı olarak ayrılmıştır.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Auth` altında; `BCryptPasswordHasher` ile şifre karşılaştırması yapar.
- Staff login endpoint'i tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Tek metotlu, minimal bir arayüzdür — staff kimlik doğrulamasının tek sorumluluğu budur; kayıt (registration) staff için bu port üzerinden yapılmaz (staff hesapları farklı bir yönetim akışıyla oluşturulur, self-servis kayıt yoktur — bu da `ICustomerAuthService.RegisterAsync`'in neden yalnızca müşteri tarafında olduğunu açıklar).

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<UserInfo?> AuthenticateAsync(string username, string password, CancellationToken ct = default)` | Kullanıcı adı+şifre ile doğrulama; başarısızsa `null`. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Auth.UserInfo`.

## Bağlantılar

- [ICustomerAuthService](ICustomerAuthService.md) — müşteri tarafındaki karşılığı.
- [ITokenService](ITokenService.md)
