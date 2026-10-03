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
- Şu anki durumda bu kurala ek yapılmıştır: `InitialCreate`'in yanında küçük, hedefli migration'lar vardır (aşağıdaki tablo). Bunlar, squash konvansiyonunun **istisnası** olarak, ana şemadan sonra eklenmiş izole değişikliklerdir. Mevcut veriyi dönüştürmesi gereken değişiklikler (ör. unique index'ten önce mükerrer kayıtları kapatmak) `InitialCreate`'e katlanamaz — o veri adımı ancak ayrı bir migration'da çalışır.
- Yeni bir şema değişikliği yapacaksanız: önce bu README'yi güncelleyen/son değişikliği yapan geliştiriciye danışın — "hep `InitialCreate`'i güncelle" kuralı mı yoksa "yeni migration aç" kuralı mı geçerli, proje aşamasına göre değişebilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Squashed-migration yaklaşımı, geliştirme aşamasında (henüz canlı kullanıcı verisi taşıyan bir üretim veritabanı yokken) onlarca küçük migration dosyası biriktirmek yerine tek, okunabilir bir şema tanımı tutmayı hedefler — `git log` üzerinden şemanın nasıl evrildiğini izlemek yerine, kod incelemesi (`InitialCreate.cs` diff'i) üzerinden izlenir.

## 6. Dosyalar

| Dosya | Ne zaman değişir |
|---|---|
| `..._InitialCreate.cs` / `.Designer.cs` | Ana şema; genellikle YENİ dosya açılmadan burada güncellenir (yukarıdaki konvansiyona bakın). |
| `..._AddApprovalExecutionStatus.cs` / `.Designer.cs` | HITL onay-yürütme durumu alanları — squash sonrası eklenen istisna. |
| `..._AddCustomerAccountUniqueness.cs` / `.Designer.cs` | Müşteri hesap tablosu benzersizlik kısıtı — squash sonrası eklenen istisna. |
| `..._AddApprovalParamSignatureDedup.cs` / `.Designer.cs` | `approval_requests.param_signature` + `ux_approvals_pending_dedup` — bekleyen onaylar için DB seviyesinde dedup. |
| `..._AddOrderDeliveredAt.cs` / `.Designer.cs` | `orders.delivered_at` kolonu. |
| `..._AddEscalationOpenDedup.cs` / `.Designer.cs` | `ix_escalations_session_open` yerine unique `ux_escalations_open_session_agent` (session + ajan başına tek açık eskalasyon). Index'ten önce mevcut mükerrer açık kayıtlardan en eskisi dışındakileri `Dismissed` olarak kapatır (silmez). Bkz. [EscalationConfiguration](../EfCore/Configurations/Hitl/EscalationConfiguration.md). |
| `CustomerSupportDbContextModelSnapshot.cs` | EF Core'un otomatik bakımını yaptığı, o anki TÜM modelin tek dosyalık özeti; `dotnet ef migrations add` her çalıştığında güncellenir. |

## 7. Bağımlılıklar

Yok (araç-üretimi kod, DI'a katılmaz).
