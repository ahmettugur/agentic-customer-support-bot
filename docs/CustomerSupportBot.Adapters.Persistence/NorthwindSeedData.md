# NorthwindSeedData

> 💡 **Analiz notu:** Northwind veritabanından alınmış demo veriler — ürünler (Chai, Chang...), kategoriler (Beverages, Dairy...), müşteriler ve siparişler. İlk kurulumda DB'yi doldurmak için kullanılır.

## Ne İşe Yarar

Northwind veritabanından türetilmiş statik demo seed verilerini (kategori, ürün, müşteri, sipariş, şikayet) sağlayan yardımcı sınıftır.

## Hangi Amaçla Kullanılır

`DemoDataSeeder` tarafından veritabanını demo verisiyle doldurmak için çağrılır.

## Sorumlulukları

- 21 kategori (gıda + elektronik) tanımlamak.
- 60+ ürün tanımlamak (fiyat, stok, kategori bilgisi ile).
- Demo müşteriler, siparişler ve sipariş kalemleri tanımlamak.
- Demo şikayet kayıtları tanımlamak.

## Diğer Katman ve Bileşenlerle İlişkileri

- **Kullanan sınıf**: [DemoDataSeeder](DemoDataSeeder.md).
- **Entity tipleri**: `CategoryEntity`, `ProductEntity`, `CustomerEntity`, `OrderEntity`, `OrderItemEntity`, `ComplaintEntity`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Statik metotlar — hiçbir bağımlılığı yok, test edilebilir. ID formatı: minimum 4 hane, prefix yok (order: 1030+, customer: 1001+, complaint: 1001+). Türkçe ürün/kategori isimleri — demo ortamı Türkçedir.

## Bağımlılıklar

Yok — saf statik veri tanımları.
