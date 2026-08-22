# UserEntity

**Dosya:** `EfCore/Entities/Auth/UserEntity.cs`
**Şema/Tablo:** `auth.users`
**Configuration:** [UserConfiguration](../../Configurations/Auth/UserConfiguration.md)

## 1. Ne İşe Yarar

Sisteme giriş yapabilen bir hesabı (Admin, Agent veya Customer rolünde) temsil eden EF Core
varlığıdır; `auth.users` tablosunun satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

`EfUserAuthRepository` (`EfCore/Auth/`) bu tabloyu okuyup/yazıp `ITokenService`/`JwtOptions`
akışına (`UserInfo` Domain modeli) besler. Hem admin/agent girişi hem müşteri chat girişi
(login) **aynı** tabloyu, `Role` alanıyla ayrışan tek bir kimlik modeli üzerinden kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Kullanıcı adı, şifre hash'i, rol ve role-özel bağ alanlarını (`LinkedAgentId`,
  `LinkedCustomerId`) taşımak.
- **Üstlenmediği:** Şifre doğrulama/hash'leme (bu `BCryptPasswordHasher`'ın işi), JWT üretimi
  (`JwtAccessTokenProvider`'ın işi) — entity sadece veri taşır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [RefreshTokenEntity](RefreshTokenEntity.md) `UserId` üzerinden buna **gerçek** foreign key ile
  bağlıdır (`Cascade` silme).
- `LinkedAgentId` → `HumanAgentEntity` (Hitl şeması) kavramsal bağı; `LinkedCustomerId` →
  `CustomerEntity` (Catalog şeması) kavramsal bağı — ikisi de gerçek FK constraint değil, sadece
  string alan + unique index.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Admin/Agent/Customer için ayrı tablolar açmak yerine **tek tablo + `Role` ayırt edici alan +
role-özel opsiyonel bağ alanları** deseni seçilmiştir — kimlik doğrulama (login, token, şifre)
mantığı tüm roller için ortaktır, sadece "bu kullanıcı hangi iş nesnesine bağlı" bilgisi
farklılaşır. Bu, aynı `ITokenService`/`JwtAccessTokenProvider` boru hattının hem admin hem
müşteri girişinde tekrar kullanılmasını sağlar.

> 🐞 **`ux_users_linked_customer_id` neden filtreli unique index:** Bir müşterinin birden fazla
> hesap açıp aynı `CustomerEntity`'ye bağlanabilmesi (ör. e-posta çalınıp ikinci bir hesap
> sessizce eklenmesi) istenmeyen bir durumdur. Ama `LinkedCustomerId` admin/agent kullanıcılarda
> her zaman `NULL`'dur ve Postgres'te `NULL` değerler unique kısıtı ihlal etmez — bu yüzden
> filtre (`WHERE linked_customer_id IS NOT NULL`) ile kısıt sadece gerçekten bağlı olan
> satırlara uygulanır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `Username` | `string` | Kullanıcı adı, **unique** (`ux_users_username`). |
| `PasswordHash` | `string` | BCrypt hash'i. |
| `Role` | `string` | `"Admin"` (varsayılan) \| `"Agent"` \| `"Customer"`. |
| `LinkedAgentId` | `string?` | Agent rolündeyse bağlı `HumanAgentEntity` kimliği. |
| `LinkedCustomerId` | `string?` | Customer rolündeyse bağlı `CustomerEntity` kimliği, **filtreli unique**. |
| `IsActive` | `bool` | Hesap aktif mi, varsayılan `true`. |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı. |
| `LastLoginAt` | `DateTime?` | Son giriş zamanı. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı. Not: varsayılan admin hesabı runtime'da `PersistenceHydrator` tarafından
seed edilir (BCrypt hash'i her ortamda ayrıca üretilir, koda gömülü sabit hash yoktur).

## Bağlantılar

- [UserConfiguration](../../Configurations/Auth/UserConfiguration.md)
- [RefreshTokenEntity](RefreshTokenEntity.md)
- [EfUserAuthRepository](../../Auth/EfUserAuthRepository.md)
- [README](../README.md)
