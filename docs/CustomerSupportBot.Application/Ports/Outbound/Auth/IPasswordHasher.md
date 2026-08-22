# IPasswordHasher

**Kaynak:** `Ports/Outbound/Auth/IPasswordHasher.cs`
**Implementasyon:** [`BCryptPasswordHasher`](../../../../CustomerSupportBot.Adapters.Persistence/Auth/BCryptPasswordHasher.md)

## 1. Ne İşe Yarar

Şifre hash'leme ve doğrulama için minimal secondary port: `Hash(password)` ve
`Verify(password, hash)`.

## 2. Hangi Amaçla Kullanılır

Kullanıcı kaydı (admin/agent/müşteri) sırasında şifre hash'lenirken, login sırasında girilen
şifre saklanan hash ile karşılaştırılırken kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca hash üretme/doğrulama.
- **Üstlenmediği:** Kullanıcı kaydının kalıcılığı — o [`IUserAuthRepository`](IUserAuthRepository.md)'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Auth/BCryptPasswordHasher` implemente eder — BCrypt.Net-Next kütüphanesini
sarar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Application katmanı hangi hash algoritmasının (BCrypt, Argon2 vb.) kullanıldığını bilmemelidir;
bu port sayesinde algoritma değişikliği tek bir adaptör dosyasıyla sınırlı kalır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string Hash(string password)` | Düz metin şifreyi hash'ler. |
| `bool Verify(string password, string hash)` | Düz metin şifrenin verilen hash ile eşleşip eşleşmediğini kontrol eder. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
