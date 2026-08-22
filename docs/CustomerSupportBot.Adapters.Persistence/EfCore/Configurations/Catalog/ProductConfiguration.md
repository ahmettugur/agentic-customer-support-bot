# ProductConfiguration

**Dosya:** `EfCore/Configurations/Catalog/ProductConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ProductEntity>`
**Entity:** [ProductEntity](../../Entities/Catalog/ProductEntity.md)

## 1. Ne İşe Yarar

`ProductEntity`'nin `catalog.products` tablosuna eşlemesini, kısıtlarını (unique index, foreign
key) tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

- **Üstlendiği:** Kolon eşlemeleri, `Name` üzerinde **unique index**, `CategoryId` foreign key
  ve silme davranışı.
- **Üstlenmediği:** Stok/fiyat doğrulama mantığı.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`CategoryEntity` ile `HasOne(...).WithMany(c => c.Products)` üzerinden ilişkilidir (bkz.
[CategoryConfiguration](CategoryConfiguration.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`ix_products_name` neden unique:** Sistemde aynı isimli iki ürün varlığı, tool'ların
> (`GetOrderStatusTool` vb.) LLM'e yanlış/belirsiz ürün eşleştirmesi yapmasına yol açabilir. Bu
> yüzden isim benzersizliği veritabanı seviyesinde zorunlu kılınmıştır — uygulama kodu bunu
> atlasa bile `DbUpdateException` fırlatılır.

`CategoryId` için `OnDelete(DeleteBehavior.Restrict)` seçilmiştir: bir kategori, içinde ürün
varken silinemez — böylece "ürünü olan ama kategorisi kaybolmuş" tutarsız veri oluşamaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ProductEntity>)` | `Id` PK; `Name` zorunlu/≤256/unique/case-insensitive; `Price` → `numeric(10,2)`; `Stock` zorunlu; `CategoryId` FK → `CategoryEntity`, `Restrict` silme. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ProductEntity](../../Entities/Catalog/ProductEntity.md)
- [CategoryConfiguration](CategoryConfiguration.md)
- [README](../README.md)
