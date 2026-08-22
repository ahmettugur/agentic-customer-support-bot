# CustomerAuthService

**Dosya:** `Services/Auth/CustomerAuthService.cs`
**Port:** `ICustomerAuthService` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Auth`

## 1. Ne İşe Yarar

Müşterilerin **kendi kendine** (self-servis) e-posta+şifre ile hesap açması (`RegisterAsync`)
ve giriş yapması (`AuthenticateAsync`) akışını yönetir. Chat'e artık anonim erişim yok — her
müşteri önce bir hesap açıp login olmak zorunda; bu servis o hesabın "sahiplik" garantisini
sağlayan taraftır.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki `/auth/customer/register` ve `/auth/customer/login` endpoint'leri bu servisi
çağırır. Başarılı `AuthenticateAsync` sonrası dönen `UserInfo`,
[`TokenPortService.IssueAsync`](TokenPortService.md)'e verilip JWT'ye çevrilir.

## 3. Sorumlulukları

- **Üstlendiği:** Kayıt girdisini doğrulamak (e-posta/şifre formatı, şifre uzunluğu), **müşteri
  kimlik sahipliğini** doğrulamak (bkz. §5 — bu servisin var oluş nedeni budur), şifreyi
  hash'leyip kullanıcı kaydı oluşturmak, login sırasında şifreyi doğrulamak.
- **Üstlenmediği:** JWT üretimi/refresh token yönetimi (bu [`TokenPortService`](TokenPortService.md)'te),
  staff (Admin/Agent) login'i (bu [`UserService`](UserService.md)'te — ayrı ama paralel bir akış).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICustomerAuthService` port'unu implemente eder.
- **Inject eder:** `IUserAuthRepository` (aynı `users` tablosu — staff ile paylaşılır, `Role`
  alanıyla ayrışır), `ICustomerRepository` (müşteri varlığı + e-posta eşleşmesi kontrolü),
  `IPasswordHasher` (BCrypt), `ILogger`.
- Staff auth zincirinin ([`UserService`](UserService.md), [`TokenPortService`](TokenPortService.md))
  AYNI JWT/refresh-token boru hattını kullanır — sıfırdan bir auth altyapısı açılmamıştır,
  sadece `Role="Customer"` claim'i ile ayrışan bir müşteri girişi eklenmiştir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`IsEmailOwnedByCustomerAsync` kontrolü olmadan bu servis bir güvenlik açığıdır.**
> Chat anonim olduğu dönemde herkes bir müşteri numarası söyleyip o müşteri adına işlem
> yaptırabiliyordu (LLM'e serbest metinden `customerId` sorduruyordu). Login zorunlu hale
> getirilirken bu açığın **login uç noktasında yeniden açılmaması** kritikti: eğer kayıt
> uç noktası sadece "bu müşteri numarası var mı?" diye sorup e-postayı doğrulamasaydı, herkes
> rastgele/bilinen bir müşteri numarasıyla hesap açıp o müşterinin JWT'sini alabilirdi — ve
> sistemin geri kalanındaki TÜM sahiplik kontrolleri (oturum sahipliği, tool sahiplik kuralları,
> `EntityVerifier`) bu token'ı doğru müşteri sanıp geçirirdi. Bu yüzden kayıt, müşterinin
> katalogdaki kayıtlı e-postasıyla eşleşen bir e-posta istemek ZORUNDADIR.
>
> **Bunun sınırı açıkça belgelenmiştir (kodda da yorum olarak var):** bu bir e-posta
> DOĞRULAMASI değil, bir eşleşme kontrolüdür. Müşterinin kayıtlı e-postasını bilen biri —
> gerçek sahip henüz kaydolmadıysa — hesabı açabilir. Ancak hesabı SADECE o e-postayla açabilir,
> kendi adresine bağlayamaz; gerçek sahip aynı adresle giriş denediğinde "bu e-postayla zaten
> hesap var" hatasıyla durumu fark eder. Tam çözüm e-posta/OTP doğrulamasıdır; bu kontrol onun
> doğal ön adımı ve mevcut saldırı yüzeyini önemli ölçüde daraltır.

Hata mesajları **bilinçli olarak belirsizdir**: "E-posta adresi ile müşteri kimlik numarası
eşleşmiyor" der, hangi alanın yanlış olduğunu söylemez. Ayrım verilseydi, geçerli müşteri
numaraları ile kayıtlı e-postalar deneme-yanılmayla (enumeration) eşleştirilebilirdi.

Aynı prensip staff auth'unda ([`UserService`](UserService.md)) da tekrarlanır: "kullanıcı yok"
ile "şifre yanlış" ayrı loglanır ama ikisi de çağırana aynı `null` sonucunu döner.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RegisterAsync(string email, string password, string customerId, CancellationToken ct = default): Task<(UserInfo? User, string? Error)>` | Girdi doğrulama → müşteri kimlik sahipliği kontrolü → şifre hash'leme → kullanıcı kaydı oluşturma. Hata varsa `User=null`, `Error` dolu döner. |
| `AuthenticateAsync(string email, string password, CancellationToken ct = default): Task<UserInfo?>` | E-posta+şifre ile login; hesap yok/pasif/rol uyuşmuyor/şifre yanlışsa `null`. |

## 7. Bağımlılıklar (Constructor Injection)

- `IUserAuthRepository` — kullanıcı (auth) tablosu erişimi; staff ile paylaşılan tablo.
- `ICustomerRepository` — müşteri varlığı (`Exists`) ve e-posta sahipliği (`IsEmailOwnedByCustomerAsync`) kontrolü.
- `IPasswordHasher` — BCrypt tabanlı hash/verify.
- `ILogger<CustomerAuthService>` — başarısız kayıt/login denemelerini loglar.

## Bağlantılar

- [TokenPortService.md](TokenPortService.md) — JWT/refresh-token üretimi
- [UserService.md](UserService.md) — staff (Admin/Agent) auth'unun paralel akışı
