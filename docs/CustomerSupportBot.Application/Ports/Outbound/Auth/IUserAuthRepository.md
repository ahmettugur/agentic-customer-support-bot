# IUserAuthRepository

**Kaynak:** `Ports/Outbound/Auth/IUserAuthRepository.cs`
**Implementasyon:** [`EfUserAuthRepository`](../../../../CustomerSupportBot.Adapters.Persistence/EfCore/Auth/EfUserAuthRepository.md)

## 1. Ne İşe Yarar

Kullanıcı (admin/agent/müşteri hesabı) kimlik doğrulama kaydının kalıcılığı için secondary port.

## 2. Hangi Amaçla Kullanılır

Login akışı kullanıcıyı `FindActiveByUsernameAsync` ile bulur; token yenileme
`FindByIdAsync`/`UpdateLastLoginAsync` kullanır; müşteri self-servis kaydı `CreateAsync`
çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Kullanıcı hesabı CRUD'unun okuma/oluşturma tarafı, son giriş zamanı takibi.
- **Üstlenmediği:** Şifre hash'leme ([`IPasswordHasher`](IPasswordHasher.md)'ın işi), token
  üretimi ([`IJwtAccessTokenProvider`](IJwtAccessTokenProvider.md)'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/EfCore/Auth/EfUserAuthRepository` implemente eder (EF Core üzerinden
Postgres `Users` tablosu). `linkedCustomerId` parametresi, `UserInfo.LinkedCustomerId` alanına
yazılır ve müşteri hesabını gerçek `CustomerEntity`'ye bağlar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`CreateAsync` username zaten alınmışsa `null` döner (exception fırlatmaz) — çağıran taraf
(`CustomerAuthService`) bunu "e-posta kullanımda" olarak yorumlayıp kullanıcıya anlamlı bir
mesaj döner; kontrol akışı exception yerine dönüş değeriyle yönetilir çünkü bu beklenen, sık
karşılaşılan bir durumdur (exception'lar istisnai durumlar için ayrılmıştır).

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<UserInfo?> FindActiveByUsernameAsync(string username, CancellationToken ct = default)` | Aktif kullanıcıyı username'e göre bulur (login). |
| `Task<UserInfo?> FindByIdAsync(string id, CancellationToken ct = default)` | Kullanıcıyı id ile bulur. |
| `Task UpdateLastLoginAsync(string id, DateTime lastLoginAt, CancellationToken ct = default)` | Son giriş zamanını günceller. |
| `Task<UserInfo?> CreateAsync(string username, string passwordHash, string role, string? linkedCustomerId, CancellationToken ct = default)` | Yeni hesap oluşturur; username kullanımdaysa `null` döner. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Auth.UserInfo`'ya bağımlıdır.
