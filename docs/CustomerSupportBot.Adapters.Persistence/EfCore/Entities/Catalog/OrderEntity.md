# OrderEntity

**Dosya:** `EfCore/Entities/Catalog/OrderEntity.cs`
**Şema/Tablo:** `catalog.orders`
**Configuration:** [OrderConfiguration](../../Configurations/Catalog/OrderConfiguration.md)

## 1. Ne İşe Yarar

Bir siparişin başlık kaydını (durum, tarih, iptal/iade bilgisi) temsil eden EF Core varlığıdır;
`catalog.orders` tablosunun satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

Sipariş verme (`OrderPlacementTool`), sipariş durumu sorgulama (`order_status` tool'u), sipariş
iptali (`OrderCancelTool`) ve iade talebi (`ReturnRequestTool`) akışlarının hepsi bu tabloyu
okur/günceller. HITL onay akışında (`ApprovalGateService`) bu 4 aksiyondan ikisi (iptal, iade)
admin onayı gerektirir.

## 3. Sorumlulukları

- **Üstlendiği:** Sipariş durumunu (`Status`: ör. "Beklemede", "Tamamlandı", "İptal Edildi") ve
  iptal/iade meta verisini (kim, ne zaman, neden) taşımak; sipariş kalemlerine (`Details`)
  gezinme.
- **Üstlenmediği:** Stok düşürme (bu `ProductEntity.Stock` üzerinde ayrı bir işlemdir), durum
  geçiş kurallarının doğrulanması (Application katmanının işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `CustomerId` → `CustomerEntity.Id`'ye mantıksal referans (FK constraint yok, bkz.
  [CustomerEntity](CustomerEntity.md) uyarısı).
- `Details` → [OrderDetailEntity](OrderDetailEntity.md) ile 1-N, `Cascade` silme (sipariş
  silinirse kalemleri de silinir).
- `ComplaintEntity.OrderId` bu entity'ye mantıksal referans verir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Birincil anahtar adı bilinçli olarak `Code` (`Id` değil) — iş dünyasında "sipariş numarası"
kavramına daha yakın bir isimlendirme. İptal ve iade, ayrı zaman damgası + neden çiftleriyle
modellenmiştir (`CancelledAt`/`CancelReason`, `ReturnRequestedAt`/`ReturnReason`) — tek bir
"durum" alanına sıkıştırmak yerine, her aksiyonun ne zaman/neden olduğu ayrı ayrı sorgulanabilir
kalır (denetim/rapor kolaylığı).

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Code` | `long` | Birincil anahtar (sipariş numarası), `UseIdentityByDefaultColumn`. |
| `CustomerId` | `long` | Siparişi veren müşteri, index'li (`ix_orders_customer_id`). |
| `Status` | `string` | Sipariş durumu metni, zorunlu, ≤64 karakter. |
| `OrderDate` | `DateTime` | Sipariş tarihi, `timestamptz` kolon tipi. |
| `CancelledAt` | `DateTime?` | İptal edildiyse zaman damgası. |
| `CancelReason` | `string?` | İptal nedeni, ≤512 karakter. |
| `ReturnRequestedAt` | `DateTime?` | İade talep edildiyse zaman damgası. |
| `ReturnReason` | `string?` | İade nedeni, ≤512 karakter. |
| `Details` | `ICollection<OrderDetailEntity>` | Sipariş kalemleri (ürün + adet). |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [OrderConfiguration](../../Configurations/Catalog/OrderConfiguration.md)
- [OrderDetailEntity](OrderDetailEntity.md), [CustomerEntity](CustomerEntity.md), [ComplaintEntity](ComplaintEntity.md)
- [README](../README.md)
