# CustomerSupportBot.Adapters.Persistence.Migrations

## 1. Ne İşe Yarar

Bu klasör, EF Core CLI (`dotnet ef migrations add/remove`) tarafından **otomatik üretilen** kod dosyalarını barındırır: her migration için bir `{timestamp}_{Ad}.cs` (yukarı/aşağı şema değişikliği), bir `{timestamp}_{Ad}.Designer.cs` (o migration anındaki model snapshot'ı) ve tek bir `CustomerSupportDbContextModelSnapshot.cs` (mevcut, en güncel tüm model şeması).

## 2. Hangi Amaçla Kullanıldığı

`dotnet ef database update` (veya uygulama açılışında otomatik migration) çalıştırıldığında PostgreSQL şemasını kod-tanımlı entity modelleriyle (`EfCore/Entities/`, `EfCore/Configurations/`) senkron tutmak.

## 3. Sorumlulukları

Bu dosyalar **elle düzenlenmez**. Skill'in "her class için ayrı dosya" kuralı burada pratik değildir çünkü bunlar insan tarafından tasarlanan sınıflar değil, `dotnet ef` tool'unun `CustomerSupportDbContext`'in mevcut model durumunu diff'leyerek ürettiği kod çıktısıdır.

## 4. Repo Konvansiyonu: Squashed Migration

⚠️ **Önemli — stajyerlerin bilmesi gereken proje-özel kural:** Bu repo, standart EF Core "her değişiklik yeni migration dosyası" akışını **kullanmaz**. Bunun yerine:

- Şema henüz üretime çıkmadığından (veya üretim geçmişinin basit tutulması istendiğinden), yeni bir alan/tablo eklemek gerektiğinde önce **mevcut `InitialCreate` migration'ı elle güncellenir** (yeni sütun/tablo eklenir), yeni bir migration dosyası açılmaz.
- Şu anki durumda bu kurala kısmen ek yapılmıştır: `InitialCreate`'in yanında iki küçük, hedefli migration daha vardır — `AddApprovalExecutionStatus` (HITL onay yürütme durumu alanları) ve `AddCustomerAccountUniqueness` (müşteri hesap tablosunda benzersizlik kısıtı). Bunlar, squash konvansiyonunun **istisnası** olarak, ana şemadan sonra üretime çıkmış küçük, izole eklemelerdir.
- Yeni bir şema değişikliği yapacaksanız: önce bu README'yi güncelleyen/son değişikliği yapan geliştiriciye danışın — "hep `InitialCreate`'i güncelle" kuralı mı yoksa "yeni migration aç" kuralı mı geçerli, proje aşamasına göre değişebilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Squashed-migration yaklaşımı, geliştirme aşamasında (henüz canlı kullanıcı verisi taşıyan bir üretim veritabanı yokken) onlarca küçük migration dosyası biriktirmek yerine tek, okunabilir bir şema tanımı tutmayı hedefler — `git log` üzerinden şemanın nasıl evrildiğini izlemek yerine, kod incelemesi (`InitialCreate.cs` diff'i) üzerinden izlenir.

## 6. Dosyalar

| Dosya | Ne zaman değişir |
|---|---|
| `..._InitialCreate.cs` / `.Designer.cs` | Ana şema; genellikle YENİ dosya açılmadan burada güncellenir (yukarıdaki konvansiyona bakın). |
| `..._AddApprovalExecutionStatus.cs` / `.Designer.cs` | HITL onay-yürütme durumu alanları — squash sonrası eklenen istisna. |
| `..._AddCustomerAccountUniqueness.cs` / `.Designer.cs` | Müşteri hesap tablosu benzersizlik kısıtı — squash sonrası eklenen istisna. |
| `CustomerSupportDbContextModelSnapshot.cs` | EF Core'un otomatik bakımını yaptığı, o anki TÜM modelin tek dosyalık özeti; `dotnet ef migrations add` her çalıştığında güncellenir. |

## 7. Bağımlılıklar

Yok (araç-üretimi kod, DI'a katılmaz).
