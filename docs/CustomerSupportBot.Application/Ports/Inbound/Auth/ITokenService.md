# ITokenService

**Dosya:** `Ports/Inbound/Auth/ITokenService.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## 1. Ne işe yarar?

JWT access/refresh token yaşam döngüsü için primary port — hem staff hem müşteri hesapları için ortak token üretim/yenileme/iptal sözleşmesi.

## 2. Hangi amaçla kullanılır?

Başarılı bir `IUserService.AuthenticateAsync`/`ICustomerAuthService.AuthenticateAsync` sonrası, Api katmanı bu portun `IssueAsync`'ini çağırıp `AuthResponse` üretir. `/auth/refresh` endpoint'i `RefreshAsync`'i, logout `RevokeAsync`'i çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Token üretimi, yenileme (rotasyon dahil) ve iptal sözleşmesini tanımlamak.
- **Üstlenmediği:** Kullanıcının kimliğinin doğrulanması (şifre kontrolü) — bu `IUserService`/`ICustomerAuthService`'in işidir; bu port yalnızca doğrulanmış bir `UserInfo`'yu token'a çevirir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu (`TokenPortService`, Application/Services/Auth) `IRefreshTokenRepository` (Outbound port) üzerinden refresh token'ları saklar/döndürür.
- Hem staff hem customer auth endpoint'leri ortak tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`RefreshAsync`, refresh token rotasyonunu **koşullu sahiplenme** (`ExecuteUpdateAsync` ile `WHERE ... AND RevokedAt == null` guard'lı atomik güncelleme) deseniyle uygular — aynı refresh token'ın eşzamanlı iki istekle iki kez kullanılıp iki farklı access token üretmesini (replay/race durumu) engeller; kayıp güncelleme durumunda `null` döner ve çağıran taraf bunu "geçersiz refresh token" olarak ele alır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default)` | Doğrulanmış bir kullanıcı için yeni access+refresh token çifti üretir. |
| `Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)` | Refresh token'ı kullanarak yeni bir token çifti üretir (rotasyonla); token geçersiz/kullanılmışsa `null`. |
| `Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)` | Refresh token'ı iptal eder (logout). |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Auth.UserInfo`.

## Bağlantılar

- [AuthResponse](AuthResponse.md)
- [IUserService](IUserService.md), [ICustomerAuthService](ICustomerAuthService.md)
