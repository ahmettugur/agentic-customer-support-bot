# UserService

**Dosya:** `Services/Auth/UserService.cs`
**Port:** `IUserService` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Auth`

## 1. Ne İşe Yarar

Staff (Admin/Agent) kullanıcılarının kullanıcı adı+şifre ile kimlik doğrulamasını yapar.
[`CustomerAuthService`](CustomerAuthService.md)'in staff tarafındaki karşılığıdır — aynı
`users` tablosunu kullanır ama kayıt akışı yoktur (staff hesapları önceden, admin tarafından
oluşturulur; self-servis kayıt yok).

## 2. Hangi Amaçla Kullanılır

Api katmanındaki `/auth/login` endpoint'i bu servisi çağırır. Başarılı sonuç
[`TokenPortService.IssueAsync`](TokenPortService.md)'e verilip JWT'ye çevrilir.

## 3. Sorumlulukları

- **Üstlendiği:** Kullanıcı adı+şifre doğrulaması, başarısız denemeleri (sebebiyle birlikte,
  ama çağırana sebep söylemeden) loglamak.
- **Üstlenmediği:** JWT üretimi (`TokenPortService`), self-servis kayıt (bu rol için yok —
  staff hesapları admin tarafından açılır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IUserService` port'unu implemente eder.
- **Inject eder:** `IUserAuthRepository`, `IPasswordHasher`, `ILogger`.
- **Kimin tarafından çağrılır:** Api katmanındaki `/auth/login` endpoint'i.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Basit ve kasıtlı olarak ince (thin) bir servistir — tüm karmaşıklık (hash doğrulama,
kullanıcı bulma) alt katmanlardadır (`IUserAuthRepository`, `IPasswordHasher`). Bu sınıfın
tek katma değeri, **iki farklı başarısızlık nedenini (kullanıcı yok / şifre yanlış) ayrı ayrı
loglarken, çağırana ikisi için de aynı `null` sonucunu döndürmesidir** — [`CustomerAuthService`](CustomerAuthService.md)'teki
aynı prensip: hata mesajı "hangi alan yanlış" bilgisini sızdırmamalı, aksi halde bir saldırgan
geçerli kullanıcı adlarını deneme-yanılmayla (enumeration) tespit edebilir.

`CustomerAuthService`'ten ayrı bir sınıf olarak tutulmasının nedeni: iki akış birbirinden
bağımsız evrilebilir olmalı (ör. staff için ileride MFA eklenirse, müşteri akışını etkilememeli)
ve iki rolün iş kuralları (customerId sahiplik kontrolü staff'ta anlamsız) doğası gereği
farklıdır — tek bir "hepsi bir arada" servis, ilgisiz iş kurallarını aynı sınıfa sıkıştırırdı.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `AuthenticateAsync(string username, string password, CancellationToken ct = default): Task<UserInfo?>` | Kullanıcı adı+şifre doğrular; hesap yok/pasif veya şifre yanlışsa `null` döner (ikisi de aynı sonuç, ayrı log). |

## 7. Bağımlılıklar (Constructor Injection)

- `IUserAuthRepository` — kullanıcı (auth) tablosu erişimi.
- `IPasswordHasher` — BCrypt tabanlı doğrulama.
- `ILogger<UserService>` — başarısız login denemelerini loglar.

## Bağlantılar

- [TokenPortService.md](TokenPortService.md) — bu servisin sonucunu JWT'ye çeviren taraf
- [CustomerAuthService.md](CustomerAuthService.md) — müşteri tarafındaki paralel akış
