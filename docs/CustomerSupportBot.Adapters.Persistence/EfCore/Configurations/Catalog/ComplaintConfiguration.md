# ComplaintConfiguration

**Dosya:** `EfCore/Configurations/Catalog/ComplaintConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ComplaintEntity>`
**Entity:** [ComplaintEntity](../../Entities/Catalog/ComplaintEntity.md)

## 1. Ne İşe Yarar

`ComplaintEntity`'nin `catalog.complaints` tablosuna eşlemesini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `OrderId` ve `CustomerId` üzerinde ayrı ayrı arama index'leri; `Code`'un
veritabanı tarafından üretilmediğini belirtmek (`ValueGeneratedNever`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`OrderEntity` ve `CustomerEntity`'ye mantıksal referans verir, ama gerçek FK constraint yoktur
(`CustomerEntity` dokümanındaki uyarıyla aynı desen).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`ValueGeneratedNever()` neden var:** Diğer Catalog entity'lerinin çoğu (`OrderEntity`,
> `CustomerEntity`) `Id`/`Code` değerini veritabanına ürettirir. `ComplaintEntity.Code` ise
> **uygulama tarafından belirlenir** (muhtemelen ilişkili sipariş kodundan türetilir). Bir
> stajyer bu satırı görmeden şikayet eklerken `Code`'u boş bırakırsa, veritabanı bir değer
> üretmeyeceği için `0` ile insert denemesi hataya (veya yanlışlıkla `Code=0` ile) sonuçlanır —
> bu yüzden `Code` her zaman çağıran kod tarafından açıkça atanmalıdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ComplaintEntity>)` | `Code` PK (`ValueGeneratedNever`); `OrderId`/`CustomerId` zorunlu + index'li; `Complaint` zorunlu/≤2048; `Status` zorunlu/≤64. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ComplaintEntity](../../Entities/Catalog/ComplaintEntity.md)
- [README](../README.md)
