# Auth DTO'ları (AuthDtos)

- **Dosya:** `Models/Auth/AuthDtos.cs`
- **Namespace:** `CustomerSupportBot.Api.Models.Auth`
- **Tipler:** `LoginRequest`, `RefreshRequest`, `LogoutRequest`, `CustomerRegisterRequest`, `CustomerLoginRequest` (hepsi `sealed record`)

## 1. Ne İşe Yarar

[AuthEndpoints](../Endpoints/Intelligence.md)'in kabul ettiği HTTP istek gövdelerini tanımlayan
saf veri taşıyıcılarıdır (DTO). Hiçbir davranış/doğrulama mantığı içermezler.

## 2. Hangi Amaçla Kullanılır

Minimal API'nin model binding'i, istek gövdesini doğrudan bu record'lara deserialize eder;
endpoint lambda'sı parametre olarak alır.

## 3. Sorumlulukları

Yalnızca alan tanımı. **Üstlenmediği:** doğrulama (email formatı, şifre uzunluğu vb. — bu
`IUserService`/`ICustomerAuthService` implementasyonlarının işi), kimliğin nasıl doğrulandığı.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [AuthEndpoints](../Endpoints/Intelligence.md) — tek tüketici.
- `IUserService.AuthenticateAsync`, `ITokenService.{IssueAsync,RefreshAsync,RevokeAsync}`,
  `ICustomerAuthService.{RegisterAsync,AuthenticateAsync}` — bu DTO'ların alanlarının aktarıldığı
  Application katmanı metotları.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

HTTP katmanına özgü DTO'lar Application katmanının domain tiplerinden **kasıtlı olarak ayrı**
tutulur: HTTP sözleşmesi (JSON alan adları, hangi alanların zorunlu olduğu) ile iç domain modeli
birbirinden bağımsız evrilebilsin diye — API sözleşmesi değişmeden domain modeli
refactor edilebilir, ya da tersi.

`CustomerRegisterRequest` müşterinin **hangi mevcut `CustomerId`'ye bağlanacağını** doğrudan
istek gövdesinden alır (`CustomerId` alanı) — bu, "bir e-posta/şifre çiftini var olan bir müşteri
kaydına bağlama" akışının parçasıdır; kayıt sırasında yeni bir müşteri OLUŞTURULMAZ, mevcut bir
`CustomerEntity`'ye kimlik doğrulama eklenir (gerçek doğrulama/eşleştirme mantığı
`ICustomerAuthService.RegisterAsync`'tedir, bu dosyada değil).

## 6. Metotlar / Üyeler

| Tip | Alanlar | Kullanıldığı Uç |
|---|---|---|
| `LoginRequest` | `Username`, `Password` | `POST /auth/login` |
| `RefreshRequest` | `RefreshToken` | `POST /auth/refresh` |
| `LogoutRequest` | `RefreshToken` | `POST /auth/logout` |
| `CustomerRegisterRequest` | `Email`, `Password`, `CustomerId` | `POST /auth/customer/register` |
| `CustomerLoginRequest` | `Email`, `Password` | `POST /auth/customer/login` |

## 7. Bağımlılıklar

Yok — saf veri tipleri.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../Endpoints/Intelligence](../Endpoints/Intelligence.md)
