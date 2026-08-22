# AuthResponse

**Dosya:** `Ports/Inbound/Auth/AuthResponse.cs`
**Tür:** `record`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## 1. Ne işe yarar?

`ITokenService`'in çıktı DTO'su — başarılı bir login/refresh sonrası döndürülen access/refresh token çiftini ve kullanıcı bilgisini taşır.

## 2. Hangi amaçla kullanılır?

Api katmanındaki `/auth/login`, `/auth/customer/login`, `/auth/refresh` gibi endpoint'ler bu tipi doğrudan JSON response olarak döner; frontend (`AuthService`, `AuthTokenStore`) bu yanıtı işleyip token'ları saklar.

## 3. Sorumlulukları

- **Üstlendiği:** Token çiftini, geçerlilik sürelerini ve kullanıcı görüntüleme bilgisini (rol, ad) taşımak.
- **Üstlenmediği:** Token'ın nasıl üretildiği/imzalandığı — bu `ITokenService` implementasyonunun (JWT + BCrypt altyapısı) işidir.

## 4. Diğer katman/bileşenlerle ilişkileri

- `ITokenService.IssueAsync`/`RefreshAsync` bu tipi döner.
- Api katmanındaki auth endpoint'leri (staff ve customer ortak) tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`FullName` alanı yalnızca `Customer` rolünde doludur; staff hesaplarında `null`dur. Bunun nedeni açık belgelenmiştir:

> Müşterinin katalogdaki adı soyadı — arayüzün kullanıcıya adıyla hitap edebilmesi için döner, `Username` (e-posta) tek başına gösterime uygun olmadığından. Kullanıcının KENDİ verisi olduğu için ek bir yetkilendirme gerektirmez; başka müşterinin adı bu yolla asla dönmez — kimlik JWT'nin bağlı olduğu hesaptan çözülür.

Aynı DTO'nun hem staff hem customer login'i için ortak kullanılması, mevcut JWT+refresh-token altyapısının sıfırdan yazılmak yerine genişletildiğini gösterir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `AccessToken` | `string` | Kısa ömürlü JWT erişim token'ı. |
| `RefreshToken` | `string` | Uzun ömürlü, erişim token'ını yenilemek için kullanılan token. |
| `AccessTokenExpiresAt` | `DateTime` | Erişim token'ının son geçerlilik zamanı. |
| `RefreshTokenExpiresAt` | `DateTime` | Refresh token'ının son geçerlilik zamanı. |
| `Username` | `string` | Giriş kimliği — müşteri hesaplarında e-posta. |
| `Role` | `string` | Kullanıcı rolü (`Admin`/`Agent`/`Customer`). |
| `LinkedAgentId` | `string?` | Rol `Agent` ise bağlı olduğu `HumanAgent` kaydının kimliği. |
| `FullName` | `string?` | Yalnızca `Customer` rolünde dolu — müşterinin adı soyadı. |

## 7. Bağımlılıklar

Yok — saf bir DTO.

## Bağlantılar

- [ITokenService](ITokenService.md)
- [ICustomerAuthService](ICustomerAuthService.md)
