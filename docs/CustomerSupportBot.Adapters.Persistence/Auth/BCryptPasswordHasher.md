# BCryptPasswordHasher

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Auth/BCryptPasswordHasher.cs`
- **Port:** `IPasswordHasher` (`CustomerSupportBot.Application.Ports.Outbound.Auth`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Auth`

## 1. Ne İşe Yarar

`BCrypt.Net-Next` kütüphanesini kullanarak parola hash'leme ve doğrulama yapan, `IPasswordHasher` portunun tek somut implementasyonudur.

## 2. Hangi Amaçla Kullanıldığı

Hem staff (admin/agent, `UserService`) hem müşteri (`ICustomerAuthService`) kayıt/login akışlarında parolaların **düz metin olarak asla saklanmaması** için kullanılır.

## 3. Sorumlulukları

- `Hash(password)` — `WorkFactor=11` ile BCrypt hash üretir (salt otomatik, BCrypt algoritmasının bir parçası).
- `Verify(password, hash)` — girilen parolayı saklı hash ile karşılaştırır; hash boş/geçersiz formattaysa (`BCrypt.Net.SaltParseException` vb.) exception fırlatmak yerine `false` döner.

**Üstlenmediği:** Parola politikası (minimum uzunluk, karmaşıklık) — bu, çağıran servis (`UserService`/`ICustomerAuthService`) veya DTO validasyonu seviyesinde uygulanır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Application katmanındaki `IPasswordHasher` portunu implemente eder — Application, somut hash algoritmasını (BCrypt) bilmez.
- `TokenService`/`ICustomerAuthService`/kayıt endpoint'leri bu servisi DI ile alır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

BCrypt algoritması, kasıtlı olarak yavaş çalışacak şekilde tasarlanmıştır (brute-force saldırılarına karşı) — `WorkFactor=11`, üretim ortamı için makul bir maliyet/güvenlik dengesidir (yorum satırında test ortamında düşürülebileceği belirtilir, ancak kod şu an sabit değer kullanır, konfigüre edilebilir değildir). `Verify`'ın `try/catch` ile `false` dönmesi — bozuk/eski formatlı bir hash'in uygulamayı çökertmek yerine "parola yanlış" gibi davranmasını sağlar (güvenlik açısından da bilgi sızdırmaz).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Hash(password)` | `BCrypt.HashPassword(password, WorkFactor)` çağırır. |
| `Verify(password, hash)` | Hash boş/geçersizse veya doğrulama exception fırlatırsa `false`; aksi halde `BCrypt.Verify` sonucu. |

## 7. Bağımlılıklar

- Yok (üçüncü parti `BCrypt.Net-Next` paketi statik olarak çağrılır, DI ile enjekte edilen bir servis değil).
