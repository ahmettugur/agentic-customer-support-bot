# RatingEntity

**Dosya:** `EfCore/Entities/Analytics/RatingEntity.cs`
**Şema/Tablo:** `analytics.ratings`
**Configuration:** [RatingConfiguration](../../Configurations/Analytics/RatingConfiguration.md)

## 1. Ne İşe Yarar

Bir sohbet oturumunun sonunda müşterinin bıraktığı değerlendirmeyi (1-5 yıldız + opsiyonel
metin geri bildirim) temsil eder.

## 2. Hangi Amaçla Kullanılır

Sohbet sonunda gösterilen değerlendirme formundan gelen veriyi kalıcı hale getirir; admin
panelindeki memnuniyet/analitik raporlarında (`SessionAnalytics` Domain modeli) kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Bir oturuma ait TEK değerlendirmeyi taşımak (birincil anahtar `SessionId`
  olduğu için aynı oturuma ikinci bir rating eklenemez — üzerine yazılır).
- **Üstlenmediği:** Değerlendirme formunun UI mantığı, yıldız aralığının doğrulanması (bu,
  veritabanı seviyesinde `CHECK` kısıtıyla garanti edilir, bkz. madde 5).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden `SessionEntity`'ye (Chat şeması) mantıksal referans verir (gerçek FK yok).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden `SessionId` PK, ayrı bir `Id` de var:** Birincil anahtar bilinçli olarak
> `SessionId`'dir — "oturum başına tek rating" iş kuralını PK'nin kendisi zorunlu kılar (aynı
> `SessionId` ile ikinci bir insert `PRIMARY KEY` ihlaline düşer, `UPSERT` gerekir). Ayrıca bir
> `Id` alanı da tutulur — bu, Domain modelindeki rating kaydının kendi kimliğidir (denetim/log
> amaçlı, PK olarak kullanılmaz).

`stars BETWEEN 1 AND 5` bir `CHECK` constraint'idir — uygulama kodu yıldız aralığını yanlış
doğrulasa/atlasa bile veritabanı 0 veya 6 gibi geçersiz bir değeri asla kabul etmez.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `SessionId` | `string` | Birincil anahtar — oturum başına tek rating. |
| `Id` | `string` | Domain modelindeki rating kaydının kendi kimliği (audit amaçlı). |
| `Stars` | `int` | 1-5 arası, `CHECK` kısıtıyla garanti edilir. |
| `Feedback` | `string?` | Opsiyonel serbest metin geri bildirim. |
| `RatedAt` | `DateTime` | Değerlendirme zamanı. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [RatingConfiguration](../../Configurations/Analytics/RatingConfiguration.md)
- [README](../README.md)
