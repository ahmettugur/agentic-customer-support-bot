# ComplaintEntity

**Dosya:** `EfCore/Entities/Catalog/ComplaintEntity.cs`
**Şema/Tablo:** `catalog.complaints`
**Configuration:** [ComplaintConfiguration](../../Configurations/Catalog/ComplaintConfiguration.md)

## 1. Ne İşe Yarar

Bir müşteri şikayet kaydını temsil eden EF Core varlığıdır; `catalog.complaints` tablosunun
satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

Şikayet kaydı oluşturma (`ComplaintRegistrationTool`, admin onayı gerektiren 4 aksiyondan biri)
ve şikayet durumu sorgulama (`complaint_status`, `get_all_complaints` tool'ları) akışlarında
kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Şikayet metnini, hangi sipariş/müşteriyle ilişkili olduğunu ve durumunu
  taşımak.
- **Üstlenmediği:** Şikayet metninin içerik/duygu analizi (bu, `SessionStateExtractor`/reasoning
  katmanının işi, ayrı bir sinyal olarak session state'te tutulur).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `OrderId` → `OrderEntity.Code`'a mantıksal referans, index'li (`ix_complaints_order_id`).
- `CustomerId` → `CustomerEntity.Id`'ye mantıksal referans, index'li
  (`ix_complaints_customer_id`). İkisinde de veritabanı seviyesinde FK constraint tanımlı değil
  (yalnızca index).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Birincil anahtar (`Code`) `ValueGeneratedNever` — yani veritabanı otomatik üretmiyor, değeri
uygulama tarafından (muhtemelen sipariş kodundan türetilerek ya da ayrı bir sayaçla) atanmalı.
Bu, `OrderEntity`/`CustomerEntity`'nin `UseIdentityByDefaultColumn` kullanmasından bilinçli bir
sapmadır — şikayet kodu üretim mantığı çağıran servis tarafında kontrol edilir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Code` | `long` | Birincil anahtar, **veritabanı tarafından üretilmez** — uygulama atar. |
| `OrderId` | `long` | İlişkili sipariş kodu, index'li. |
| `CustomerId` | `long` | Şikayeti açan müşteri, index'li. |
| `Complaint` | `string` | Şikayet metni, zorunlu, ≤2048 karakter. |
| `Status` | `string` | Şikayet durumu, zorunlu, ≤64 karakter. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [ComplaintConfiguration](../../Configurations/Catalog/ComplaintConfiguration.md)
- [OrderEntity](OrderEntity.md), [CustomerEntity](CustomerEntity.md)
- [README](../README.md)
