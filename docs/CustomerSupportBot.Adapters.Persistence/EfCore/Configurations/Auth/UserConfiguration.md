# UserConfiguration

**Dosya:** `EfCore/Configurations/Auth/UserConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<UserEntity>`
**Entity:** [UserEntity](../../Entities/Auth/UserEntity.md)

## 1. Ne İşe Yarar

`UserEntity`'nin `auth.users` tablosuna eşlemesini, en kritik olarak iki unique index'ini
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `Username` üzerinde unique index; `LinkedCustomerId` üzerinde **filtreli**
unique index (bir müşteriye tek hesap kuralı).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Yok (bu tablo başka bir Configuration'a FK ile bağlı değildir; [RefreshTokenConfiguration](RefreshTokenConfiguration.md)
buna bağlıdır).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Kod içindeki yorum satırları bu kararların gerekçesini açıkça anlatıyor — bkz.
[UserEntity](../../Entities/Auth/UserEntity.md) 🐞 notu: `ux_users_linked_customer_id` filtreli
unique index'i, "bir müşteriye yalnızca bir hesap" kuralını uygulama kodu unutsa bile
veritabanı seviyesinde garanti eder; e-posta eşleşmesine güvenmek tek başına yetmez çünkü
e-postayı bilen biri gerçek sahipten önce kayıt olabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<UserEntity>)` | `Id` PK; `Username` zorunlu/unique; `PasswordHash`/`Role` zorunlu; `LinkedAgentId`/`LinkedCustomerId` opsiyonel (ikincisi filtreli unique); `IsActive` varsayılan `true`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [UserEntity](../../Entities/Auth/UserEntity.md)
- [RefreshTokenConfiguration](RefreshTokenConfiguration.md)
- [README](../README.md)
