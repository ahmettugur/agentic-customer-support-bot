# PerformancePanel.razor

**Dosya:** `Components/PerformancePanel.razor` · Admin sayfasının **Performans** bölümü (`/admin?tab=performance`), yalnız yönetici.

- Dönem çipleri: Son 24 saat · 7 gün (varsayılan) · 30 gün · 90 gün; sağda dönemin tarih aralığı.
- Üstte Analiz sekmesinin kartları (`analytics-summary`): asistan turu, asistanın yanıtladığı tur, temsilciye aktarılan,
  hatayla biten, ortalama tur süresi, araç hata oranı.
- Tablolar: **Yapay zekâ ajanları** (tur, süre, görev sonuçları, başarı rozeti — ≥ %80 yeşil, ≥ %50 sarı, altı kırmızı),
  **Araçlar** (en çok hata veren üstte, son hata mesajı), **Temsilciler** (üstlenilen/çözülen, ilk yanıt, çözüm süresi,
  kapatılan şikayet, puan), **Turlar nasıl bitti** (yatay çubuklar).
- Admin sayfasının 15 sn'lik yenilemesi `RefreshAsync`'i çağırır; dönem "şimdi"ye göre kayar.
- Biçimler `Helpers/PerformanceView.cs` (testli: `PerformanceViewTests`). Ondalıklar kültürden bağımsız yazılır
  ("9,3 sn"): Blazor WASM tarayıcıda "tr-TR" ICU verisini yüklemediği için kültüre bağlı biçim "9.3" veriyordu.
