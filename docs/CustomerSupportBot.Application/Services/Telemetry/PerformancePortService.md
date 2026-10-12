# PerformancePortService (`IPerformancePort`)

**Dosya:** `Services/Telemetry/PerformancePortService.cs` · Uç: `GET /analytics/performance?from=&to=` (Admin) ·
Ekran: Admin → **Performans** ([PerformancePanel](../../../CustomerSupportBot.Web/Components/PerformancePanel.md)).

Depodaki ([`IPerformanceStore`](../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresPerformanceStore.md))
gruplanmış satırları oranlara ve ekran satırlarına çevirir.

- **Aralık:** varsayılan son 7 gün; en fazla 92 gün; başlangıç bitişten önce olmalı (aksi `ArgumentException` → 400).
- **Özet:** asistan turu; *asistanın yanıtladığı tur* (hatasız ve "tamamlandı" ya da "müşteriden bilgi bekleniyor" ile
  biten — yalnızca trace'in kapanması ölçü değildir, iptal ve "bulunamadı" da kapanır); temsilciye aktarılan; hatayla
  biten; ortalama tur süresi; araç hata oranı.
- **Yapay zekâ ajanları:** tur sayısı ve ortalama süre (ajan ziyaretleri), uzmanların bildirdiği görev sonuçları
  gruplanır: *başarılı* = tamamlanan + onaya gönderilen, *aktarım* = `needs_escalation`, *takip* = `needs_followup` /
  `partial`, *başarısız* = `failed`. Başarı oranı = başarılı / görev; planlama ve yanıt ajanları görev bildirmez (null).
  Sıra akıştaki sıradır (planlama → uzmanlar → yanıt).
- **Araçlar:** çağrı, hata, hata oranı, son hata (kod + mesaj); en çok hata veren üstte.
- **Temsilciler:** üstlenilen / çözülen eskalasyon, ortalama ilk yanıt ve çözüm süresi, kapatılan şikayet, müşteri
  puanı. Hiç işi olmayan **aktif** temsilciler de sıfırla listelenir; kayıtta olmayan kimlik olduğu gibi gösterilir.

Testler: `PerformancePortServiceTests`.
