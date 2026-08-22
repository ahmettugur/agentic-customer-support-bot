# RefreshTokenConfiguration

**Dosya:** `EfCore/Configurations/Auth/RefreshTokenConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<RefreshTokenEntity>`
**Entity:** [RefreshTokenEntity](../../Entities/Auth/RefreshTokenEntity.md)

## 1. Ne İşe Yarar

`RefreshTokenEntity`'nin `auth.refresh_tokens` tablosuna eşlemesini, `UserEntity`'ye foreign
key'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `TokenHash` üzerinde unique index (token çakışmasını önler); `UserId` üzerinde
arama index'i; `UserEntity`'ye `Cascade` foreign key.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`UserConfiguration`'a `Cascade` silme ile bağlıdır — bkz. [UserConfiguration](UserConfiguration.md).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ux_refresh_tokens_hash` unique index'i, teorik olarak iki farklı kullanıcı için aynı hash'in
üretilmesini (kriptografik çakışma) veritabanı seviyesinde de imkansız kılar — savunma
katmanlarından biri.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<RefreshTokenEntity>)` | `Id` PK; `UserId`/`TokenHash` zorunlu (`TokenHash` unique); `ExpiresAt`/`CreatedAt` zorunlu `timestamptz`; `RevokedAt`/`ReplacedByTokenHash` opsiyonel; `UserEntity`'ye FK, `Cascade`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [RefreshTokenEntity](../../Entities/Auth/RefreshTokenEntity.md)
- [UserConfiguration](UserConfiguration.md)
- [README](../README.md)
