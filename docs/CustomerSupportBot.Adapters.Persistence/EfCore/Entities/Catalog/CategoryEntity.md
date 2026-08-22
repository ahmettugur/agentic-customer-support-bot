# CategoryEntity

**Dosya:** `EfCore/Entities/Catalog/CategoryEntity.cs`
**Şema/Tablo:** `catalog.categories`
**Configuration:** [CategoryConfiguration](../../Configurations/Catalog/CategoryConfiguration.md)

## 1. Ne İşe Yarar

Ürün kataloğundaki bir kategoriyi (ör. "Elektronik", "Gıda") temsil eden EF Core varlığıdır.
Veritabanındaki `catalog.categories` tablosunun satırlarına birebir karşılık gelir.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext` üzerinden ürün sorgularında (`ProductEntity.Category` navigasyonu ile)
ve kategoriye göre ürün listeleme akışlarında kullanılır. Uygulama katmanına `ProductInfo`/
`CategoryProducts` (Domain modelleri) olarak map'lenir — bu entity'nin kendisi Application/Domain
katmanına asla sızmaz (hexagonal mimarinin kuralı).

## 3. Sorumlulukları

- **Üstlendiği:** Bir kategori satırının kimliğini (`Id`) ve adını (`Name`) taşımak; ilişkili
  ürünlere (`Products`) gezinme imkanı vermek.
- **Üstlenmediği:** Doğrulama (validation), iş kuralı, DTO dönüşümü — bunlar Application
  katmanındaki mapping kodunda yapılır. Bu sınıf saf bir veri taşıyıcıdır (anemic model,
  EF Core'un beklediği şekilde).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `CategoryConfiguration` (Fluent API) bu entity'nin tablo/kolon/kısıt eşlemesini tanımlar.
- `ProductEntity.CategoryId` → `CategoryEntity.Id` foreign key ilişkisiyle bağlıdır (bkz.
  [ProductEntity](ProductEntity.md)).
- `CustomerSupportDbContext` (`EfCore/CustomerSupportDbContext.cs`) `DbSet<CategoryEntity>`
  olarak expose eder.
- Repository katmanında (`Adapters.Persistence/EfCore/...Repository.cs`) sorgulanır, Domain'e
  map'lenir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride Domain katmanı hiçbir ORM/altyapı bağımlılığı taşımaz; bu yüzden EF Core'un
ihtiyaç duyduğu (parametresiz constructor, mutable property, navigation collection) şekle sahip
ayrı bir "Entity" sınıfı tanımlanır — Domain'deki saf modellerden (`ProductInfo` vb.) bilinçli
olarak ayrılmıştır. `sealed` olması, EF Core'un proxy/lazy-loading üretmesine gerek olmadığını
(explicit loading/eager `Include` kullanıldığını) gösterir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `int` | Birincil anahtar, veritabanı tarafından otomatik üretilir (`ValueGeneratedOnAdd`). |
| `Name` | `string` | Kategori adı, zorunlu, en fazla 256 karakter, case/aksan-duyarsız collation ile aranır (bkz. Configuration). |
| `Products` | `ICollection<ProductEntity>` | Bu kategoriye ait ürünler — 1-N navigasyon koleksiyonu, varsayılan boş liste. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı, hiçbir servis inject edilmez. Tek bağımlılığı `ProductEntity` tipine olan
referanstır (navigasyon property için).

## Bağlantılar

- [CategoryConfiguration](../../Configurations/Catalog/CategoryConfiguration.md) — Fluent API eşlemesi
- [ProductEntity](ProductEntity.md) — 1-N ilişkili ürün varlığı
- [README](../README.md) — Entities klasör indeksi
