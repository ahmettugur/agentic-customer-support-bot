# ICustomerAuthService

**Dosya:** `Ports/Inbound/Auth/ICustomerAuthService.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## 1. Ne işe yarar?

Müşteri hesabı kaydı (register) ve kimlik doğrulama (login) için primary port — staff kullanıcılarından (`IUserService`) ayrı, müşteriye özgü bir kimlik doğrulama sözleşmesi.

## 2. Hangi amaçla kullanılır?

Api katmanındaki `/auth/customer/register` ve `/auth/customer/login` endpoint'leri bu portu çağırır. Başarılı `AuthenticateAsync` sonrası dönen `UserInfo`, `ITokenService.IssueAsync` ile token'a çevrilir.

## 3. Sorumlulukları

- **Üstlendiği:** Müşteri hesabının kayıt ve doğrulama iş kurallarını (e-posta benzersizliği, mevcut `CustomerEntity` ile eşleştirme, şifre doğrulama) sözleşme olarak tanımlamak.
- **Üstlenmediği:** Token üretimi — bu `ITokenService`'in işidir; bu port yalnızca "bu email+şifre geçerli bir müşteri mi" sorusunu cevaplar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Auth` altında; müşteri hesap kaydını (email+şifre hash) ve mevcut `CustomerEntity`'yi (katalogdaki müşteri kaydı) ilişkilendirir.
- Api katmanındaki customer-auth endpoint'leri tüketicisidir.
- `IChatPort`/`ChatRequest.CustomerId` zincirinin güvenlik temelini oluşturur — bu servisle doğrulanan kimlik, JWT claim'i olarak sisteme girer.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Staff (`IUserService`) ve müşteri (`ICustomerAuthService`) kimlik doğrulamasının ayrı arayüzler olarak tutulması, admin/agent yetkilendirmesiyle müşteri yetkilendirmesinin birbirine karışmamasını sağlar — iki farklı hesap tablosu (`UserEntity` vs. yeni müşteri hesap tablosu) ve iki farklı iş kuralı seti vardır. Bu ayrım, "bir müşteri hesabı yanlışlıkla admin izinleri kazanabilir mi" sınıfı hataları mimari düzeyde imkânsız kılar.

`RegisterAsync`'in `(UserInfo? User, string? Error)` tuple dönüşü, başarısızlık nedenini (e-posta zaten kullanımda / geçersiz `customerId`) çağırana taşımak için tercih edilmiştir — exception fırlatmak yerine beklenen bir iş-kuralı sonucu olarak modellenmiştir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<(UserInfo? User, string? Error)> RegisterAsync(string email, string password, string customerId, CancellationToken ct = default)` | Yeni müşteri hesabı kaydı. E-posta zaten kullanımdaysa veya `customerId` (mevcut `CustomerEntity`) geçerli değilse `User=null` ve `Error` doldurulmuş döner. |
| `Task<UserInfo?> AuthenticateAsync(string email, string password, CancellationToken ct = default)` | E-posta+şifre ile doğrulama; başarısızsa `null`. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Auth.UserInfo`.

## Bağlantılar

- [ITokenService](ITokenService.md), [AuthResponse](AuthResponse.md)
- [IUserService](IUserService.md) — staff tarafındaki karşılığı.
