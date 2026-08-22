# OrderDetailConfiguration

**Dosya:** `EfCore/Configurations/Catalog/OrderDetailConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<OrderDetailEntity>`
**Entity:** [OrderDetailEntity](../../Entities/Catalog/OrderDetailEntity.md)

## 1. Ne İşe Yarar

`OrderDetailEntity`'nin `catalog.order_details` tablosuna eşlemesini, composite anahtarını ve
iki foreign key'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Composite primary key tanımı (`OrderCode`, `ProductId`); `Order` ve `Product` ile foreign key
ilişkileri, farklı silme davranışlarıyla.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `Order` → [OrderConfiguration](OrderConfiguration.md), `Cascade` silme.
- `Product` → [ProductConfiguration](ProductConfiguration.md), `Restrict` silme.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

İki foreign key'in silme davranışı bilinçli olarak **farklıdır**: bir sipariş silinirse
kalemlerinin de silinmesi mantıklıdır (`Cascade`), ama bir ürün — geçmişte satılmış olsa bile —
asla silinip sipariş geçmişini bozmamalıdır (`Restrict`). Bu iki farklı kararın aynı dosyada
yan yana görünmesi bilinçli bir tasarım tercihidir, hata değildir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<OrderDetailEntity>)` | `HasKey(OrderCode, ProductId)` composite PK; `Quantity` zorunlu; `Order` FK `Cascade`; `Product` FK `Restrict`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [OrderDetailEntity](../../Entities/Catalog/OrderDetailEntity.md)
- [OrderConfiguration](OrderConfiguration.md), [ProductConfiguration](ProductConfiguration.md)
- [README](../README.md)
