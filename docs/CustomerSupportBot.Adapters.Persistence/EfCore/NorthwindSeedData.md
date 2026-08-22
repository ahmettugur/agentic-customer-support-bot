# NorthwindSeedData

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/NorthwindSeedData.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`NorthwindSeedData`, e-ticaret müşteri destek senaryolarını (stok sorgulama, sipariş durumu, şikayet, iptal vb.) gerçekçi verilerle test edebilmek için Northwind veri setinden uyarlanmış 8 kategori, 77 ürün, 5 müşteri ve onlarca sipariş içeren statik veri kataloğudur.

## Hangi amaçla kullanılır`?

[DemoDataSeeder](DemoDataSeeder.md) tarafından kullanılarak `catalog.categories`, `catalog.products`, `catalog.customers` ve `catalog.orders` tablolarına başlangıç verilerini sağlamak.

## Metotlar

| Metot | Dönüş Tipi | Açıklama |
|---|---|---|
| `GetCategories()` | `List<CategoryEntity>` | 8 adet e-ticaret kategorisi (İçecekler, Baharatlar, Tatlılar, Süt Ürünleri, Tahıllar, Et/Tavuk, Sebze/Meyve, Deniz Ürünleri). |
| `GetProducts()` | `List<ProductEntity>` | 77 adet ürün (fiyat, stok ve kategori ilişkileriyle). |
| `GetCustomers()` | `List<CustomerEntity>` | 5 adet müşteri profili (ad, e-posta, telefon, adres). |
| `GetOrders()` | `List<OrderEntity>` | Örnek siparişler ve sipariş detayları (`OrderDetails`). |
