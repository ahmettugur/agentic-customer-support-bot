# ProductEntity

**Dosya:** `EfCore/Entities/Catalog/ProductEntity.cs`
**Şema/Tablo:** `catalog.products`
**Configuration:** [ProductConfiguration](../../Configurations/Catalog/ProductConfiguration.md)

## 1. Ne İşe Yarar

Kataloğdaki bir ürünü (ad, fiyat, stok, kategori) temsil eden EF Core varlığıdır;
`catalog.products` tablosunun satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

Sipariş oluşturma akışında stok kontrolü/düşümü (`OrderToolsService`), ürün arama/öneri
akışlarında (`ProductRecommendationContextProvider` vb.) ve admin panelindeki ürün
listelemede sorgulanır.

## 3. Sorumlulukları

- **Üstlendiği:** Ürün kimliği, adı, fiyatı, stok miktarı ve kategori ilişkisini taşımak;
  o ürüne ait sipariş kalemlerine (`OrderDetails`) gezinme.
- **Üstlenmediği:** Stok düşürme mantığı (bu, repository/service katmanında `ExecuteUpdateAsync`
  ile atomik yapılır), fiyatlandırma kuralları, doğrulama.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `CategoryId` → `CategoryEntity.Id` foreign key (`DeleteBehavior.Restrict` — kategori silinirse
  ürün silinemez, önce ürünler taşınmalı).
- `OrderDetailEntity.ProductId` bu entity'ye referans verir (bkz. [OrderDetailEntity](OrderDetailEntity.md)).
- Repository katmanı bunu Domain'deki `ProductInfo` modeline map'ler.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Name` alanında **unique index** (`ix_products_name`) vardır — sistemde aynı isimde iki ürün
olamaz, bu bir iş kuralıdır ve veritabanı seviyesinde garanti altına alınmıştır (uygulama
kodu unutsa/atlasa bile bozulmaz). `Price` `numeric(10,2)` olarak eşlenir — `decimal` kullanımı
para birimlerinde `float`/`double` yuvarlama hatalarını önlemek içindir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `int` | Birincil anahtar, otomatik üretilir. |
| `Name` | `string` | Ürün adı, zorunlu, ≤256 karakter, **unique** (`ix_products_name`), case/aksan-duyarsız arama. |
| `Price` | `decimal` | Birim fiyat, `numeric(10,2)` kolon tipiyle eşlenir. |
| `Stock` | `int` | Mevcut stok adedi. |
| `CategoryId` | `int` | Foreign key → `CategoryEntity.Id`. |
| `Category` | `CategoryEntity` | Navigasyon — ait olduğu kategori. |
| `OrderDetails` | `ICollection<OrderDetailEntity>` | Bu ürünü içeren sipariş kalemleri. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı. `CategoryEntity` ve `OrderDetailEntity` tiplerine navigasyon referansı taşır.

## Bağlantılar

- [ProductConfiguration](../../Configurations/Catalog/ProductConfiguration.md)
- [CategoryEntity](CategoryEntity.md), [OrderDetailEntity](OrderDetailEntity.md)
- [README](../README.md)
