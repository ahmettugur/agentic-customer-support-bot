# OrderConfiguration

**Dosya:** `EfCore/Configurations/Catalog/OrderConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<OrderEntity>`
**Entity:** [OrderEntity](../../Entities/Catalog/OrderEntity.md)

## 1. Ne İşe Yarar

`OrderEntity`'nin `catalog.orders` tablosuna eşlemesini (durum, tarih, iptal/iade alanları)
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `CustomerId` üzerinde arama index'i; `timestamptz` kolon tipleriyle zaman
damgalarının saat dilimi bilgisini korumak.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`OrderDetailConfiguration` bu entity'ye `Cascade` silme ile bağlıdır (bkz.
[OrderDetailConfiguration](OrderDetailConfiguration.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Tüm zaman damgaları (`OrderDate`, `CancelledAt`, `ReturnRequestedAt`) `timestamptz` (timestamp
with time zone) olarak eşlenir — sunucu/istemci farklı saat dilimlerinde çalışsa bile UTC'ye
göre tutarlı karşılaştırma yapılabilir; `timestamp` (saat dilimsiz) kullanılsaydı çoklu bölgeli
deployment'ta zaman kayması riski olurdu.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<OrderEntity>)` | `Code` PK (identity); `CustomerId` zorunlu + index; `Status` zorunlu/≤64; `OrderDate` zorunlu/`timestamptz`; `CancelledAt`/`CancelReason`/`ReturnRequestedAt`/`ReturnReason` opsiyonel. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [OrderEntity](../../Entities/Catalog/OrderEntity.md)
- [OrderDetailConfiguration](OrderDetailConfiguration.md)
- [README](../README.md)
