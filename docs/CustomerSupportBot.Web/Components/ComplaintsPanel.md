# ComplaintsPanel.razor

**Dosya:** `Components/ComplaintsPanel.razor` · Admin sayfasının "Şikayetler" bölümü (`/admin?tab=complaints`), yönetici ve temsilci.

- **Liste:** durum filtresi (Açık / tek durum / Tümü), temsilcide "Yalnız bana atananlar"; satırda yaş, atanan ve SLA
  rengi (sunucunun `sla` alanı: `ok` / `warn` / `breach`). 20'li sayfalama.
- **Ayrıntı:** metin, sipariş özeti, fotoğraflar ([`ApprovalPhotos`](../../CustomerSupportBot.Web/README.md)), müşteriye
  gönderilen açıklama, zaman çizelgesi (iç notlar dahil).
- **Düğmeler:** sunucunun döndürdüğü `actions` listesi — kural istemcide tekrar yazılmaz.
- **İki ayrı alan:** "Müşteriye gönderilecek açıklama" yalnızca Çözüldü/Reddet yapılabiliyorsa görünür; "İç not" alanı
  not ekleme, yeniden açma gerekçesi ve kapanıştaki isteğe bağlı iç not içindir. Ortak tek kutu, müşteriye yazılan
  metnin iç not olarak kaydedilmesine yol açabiliyordu. Düğme etkinliği `ComplaintView.CanSubmit`.
- Her eylem `expectedVersion` gönderir; 409'da kayıt yenilenir ve uyarı gösterilir.
- Admin sayfasının 15 sn'lik yenilemesi `RefreshAsync`'i çağırır; rozet (`StaffBadgeState.OpenComplaints`) admin için
  bekleyen, temsilci için bekleyen + kendisine atanan sayıdır.

Yardımcılar: `Helpers/ComplaintView.cs` (etiketler, yaş, rozet, alan kuralları) — `ComplaintViewTests`.
