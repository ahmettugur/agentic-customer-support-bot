# PersistenceHydrator

**Dosya:** `EfCore/PersistenceHydrator.cs`  
**Implements:** `IHostedService`  
**Yaşam döngüsü:** Singleton (Postgres modunda kayıtlı)

## Ne yapar?

Uygulama başladığında çalışan startup kurtarma servisi. Önceki çalışmadan kalan tutarsız kayıtları
temizler. **Demo verisi seed etmez** — o iş `DemoDataSeeder`'ındır.

> 💡 **Analiz notu:** Bir restoranın sabah açılış rutini gibi — "dün gece yarıda kalan işleri kapat". Kapsamı dar tutmak önemli: yalnızca restart yüzünden **sahibi kalmamış** kayıtlar düzeltilir. Müşterinin dün verdiği ve hâlâ geçerli olan bir talep (bekleyen onay) buna dahil DEĞİLDİR — bu ayrımı kaybetmek, her deploy'da bekleyen onayların sessizce reddedilmesine yol açmıştı.

---

## Çalışma zamanı

`StartAsync(CancellationToken)` uygulama başlatma sırasında çağrılır. `StopAsync` işlem yapmaz.

---

## Adımlar

> **Onay kayıtlarına DOKUNULMAZ.** Burada eskiden 10 saniyeden eski `Pending` onayları
> `Expired`'a çeken bir adım vardı. Onayın tool çağrısını **bloklamadığı** yeni modelde bu
> yanlıştır: bekleyen bir onayın süreç-içi sahibi yoktur ve olmaması normaldir — admin günler
> sonra karar verebilir (`ApprovalOptions.StalePendingHours`, varsayılan 72 saat). O adım
> kalsaydı her deploy, bekleyen tüm onayları sessizce reddederdi. Süresi geçen kayıtları artık
> `StaleApprovalSweepService` periyodik olarak temizler.

### 1. In-flight traces terminate

```
observability.reasoning_traces
WHERE completed_at IS NULL

→ termination_reason = 'terminated_by_restart'
   error = 'terminated_by_restart'
   completed_at = NOW()
```

Restart sırasında aktif olan reasoning akışları tamamlanmadan kesilmiştir. Bu step onları kapalı olarak işaretler; istatistikler bozulmaz.

---

## Seed BURADA DEĞİL

Demo/başlangıç verisi (default admin, human agent'lar, Northwind ürün/müşteri/sipariş verisi,
demo müşteri hesabı) bu servisin işi **değildir** — ayrı bir `IHostedService` olan
**`DemoDataSeeder`** tarafından yapılır; bkz. [DemoDataSeeder.md](DemoDataSeeder.md).

İkisi farklı sorumluluklardır ve karıştırılmamalıdır:

| | PersistenceHydrator | DemoDataSeeder |
|---|---|---|
| Amaç | Restart'ın arkada bıraktığı **tutarsız kayıtları** düzeltmek | Boş bir ortamda hızlı başlamak |
| Gerekli mi | Veri bütünlüğü için evet | Hayır, kolaylık |
| Üretimde | Çalışır | Çalışmamalı |

Varsayılan şifreler ve demo hesaplarla ilgili güvenlik notları da `DemoDataSeeder` belgesindedir.

---

## Neden sadece Postgres?

InMemory adaptörler her restart'ta sıfırlanır — "kalan kayıt" kavramı yoktur. Hydrator yalnızca Postgres modunda anlamlıdır; `AddPersistenceAdapters()` çağrısında `PersistenceHydrator` otomatik kayıt edilir.
