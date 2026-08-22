# OrderDetailEntity

**Dosya:** `EfCore/Entities/Catalog/OrderDetailEntity.cs`
**Şema/Tablo:** `catalog.order_details`
**Configuration:** [OrderDetailConfiguration](../../Configurations/Catalog/OrderDetailConfiguration.md)

## 1. Ne İşe Yarar

Bir siparişin içindeki tek bir kalemi (hangi üründen kaç adet) temsil eden EF Core varlığıdır;
`catalog.order_details` tablosunun satır karşılığı, `OrderEntity` ile `ProductEntity` arasındaki
çok-çoğa ilişkiyi çözen bir "join/ilişki" tablosudur.

## 2. Hangi Amaçla Kullanılır

Sipariş oluşturma sırasında her ürün satırı için bir kayıt üretilir; sipariş detayı sorgulanırken
(`get_last_order`, `get_all_orders` tool'ları) `OrderEntity.Details` üzerinden okunur.

## 3. Sorumlulukları

- **Üstlendiği:** Sipariş-ürün-adet üçlüsünü taşımak.
- **Üstlenmediği:** Fiyat hesaplama (ürünün güncel fiyatı `ProductEntity.Price`'tan okunur, bu
  tabloda fiyat **satırda saklanmaz** — yani ürün fiyatı sonradan değişirse geçmiş sipariş
  kalemlerinin "o anki fiyatı" bu şemada ayrıca tutulmuyor).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Composite primary key: `(OrderCode, ProductId)` — aynı siparişte aynı üründen sadece bir satır
  olabilir (adet artırılarak güncellenir, yeni satır eklenmez).
- `Order` → [OrderEntity](OrderEntity.md), `Cascade` silme.
- `Product` → [ProductEntity](ProductEntity.md), `Restrict` silme (ürün, geçmiş sipariş
  kalemi varsa silinemez).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Composite key kullanımı, klasik "sipariş satırı" modellemesine göre daha kısıtlayıcı ama basit:
aynı ürün aynı siparişte tekrar edemez kuralı veritabanı seviyesinde garanti edilir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `OrderCode` | `long` | Composite key parçası, FK → `OrderEntity.Code`. |
| `ProductId` | `int` | Composite key parçası, FK → `ProductEntity.Id`. |
| `Quantity` | `int` | Sipariş edilen adet. |
| `Order` | `OrderEntity` | Navigasyon — ait olduğu sipariş. |
| `Product` | `ProductEntity` | Navigasyon — sipariş edilen ürün. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [OrderDetailConfiguration](../../Configurations/Catalog/OrderDetailConfiguration.md)
- [OrderEntity](OrderEntity.md), [ProductEntity](ProductEntity.md)
- [README](../README.md)
