# CategoryConfiguration

**Dosya:** `EfCore/Configurations/Catalog/CategoryConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<CategoryEntity>`
**Entity:** [CategoryEntity](../../Entities/Catalog/CategoryEntity.md)

## 1. Ne İşe Yarar

`CategoryEntity`'nin `catalog.categories` tablosuna nasıl eşleneceğini (kolon adları, uzunluklar,
collation) EF Core'un Fluent API'siyle tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` içinde `ApplyConfigurationsFromAssembly` ile otomatik
keşfedilip uygulanır — geliştirici bunu elle çağırmaz.

## 3. Sorumlulukları

- **Üstlendiği:** Tablo adı/şema, birincil anahtar, kolon adları/tipleri/uzunlukları, collation.
- **Üstlenmediği:** İş kuralı doğrulaması — bu sadece şema/kısıt tanımıdır, veri doğrulaması
  değildir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Schemas.Catalog` sabitini (`EfCore/Schemas.cs`) kullanarak şema adını merkezi bir yerden alır.
`CategoryEntity` ile 1-1 eşleşir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Fluent API tercih edilmiştir (Data Annotations yerine) — Domain/Entity sınıflarının EF Core'a
dair hiçbir attribute taşımaması, altyapı detaylarının (kolon adı, uzunluk) tamamen bu ayrı
Configuration sınıflarında toplanması için (Separation of Concerns).

> 🐞 **`UseCollation("und-u-ks-level1")` neden var:** PostgreSQL varsayılan collation'ı
> case-sensitive ve aksan-duyarlıdır ("Elektronik" ≠ "elektronik" ≠ "Elektronık"). Bir stajyerin
> "kategori bulunamadı" hatasıyla karşılaşmaması için ICU tabanlı bu collation, karşılaştırmaları
> case-insensitive + aksan-insensitive yapar — kullanıcı "elektronik" yazsa da eşleşir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<CategoryEntity>)` | Tek metot (arayüz gereği). `Id`'yi PK yapar (otomatik artan), `Name`'i zorunlu/≤256 karakter/case-insensitive collation ile tanımlar. |

## 7. Bağımlılıklar

Yok — kendi başına bir yapılandırma sınıfı, DI ile inject edilmez; EF Core assembly tarama ile
bulur ve çağırır.

## Bağlantılar

- [CategoryEntity](../../Entities/Catalog/CategoryEntity.md)
- [README](../README.md)
