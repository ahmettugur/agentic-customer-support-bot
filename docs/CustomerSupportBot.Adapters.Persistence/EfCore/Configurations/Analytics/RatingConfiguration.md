# RatingConfiguration

**Dosya:** `EfCore/Configurations/Analytics/RatingConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<RatingEntity>`
**Entity:** [RatingEntity](../../Entities/Analytics/RatingEntity.md)

## 1. Ne İşe Yarar

`RatingEntity`'nin `analytics.ratings` tablosuna eşlemesini, `CHECK` kısıtını ve azalan
zaman index'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `stars` için `CHECK (stars BETWEEN 1 AND 5)`; `RatedAt` üzerinde azalan
index (en yeni değerlendirmeler önce).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız bir tablo — başka bir Configuration'a FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ToTable(t => t.HasCheckConstraint(...))` sözdizimi EF Core'un tablo seviyesinde ham SQL
kısıtı tanımlama yoludur — Fluent API'de doğrudan karşılığı olmayan (`BETWEEN` gibi) kısıtlar
için kullanılır. Bkz. [RatingEntity](../../Entities/Analytics/RatingEntity.md) madde 5.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<RatingEntity>)` | `SessionId` PK; `Id`/`Stars`/`RatedAt` zorunlu; `Feedback` opsiyonel; `stars` `CHECK` kısıtı; `RatedAt` azalan index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [RatingEntity](../../Entities/Analytics/RatingEntity.md)
- [README](../README.md)
