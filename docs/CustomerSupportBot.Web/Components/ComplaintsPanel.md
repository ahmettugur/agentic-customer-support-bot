# ComplaintsPanel.razor

**Dosya:** `Components/ComplaintsPanel.razor` · Admin sayfasının "Şikayetler" bölümü (`/admin?tab=complaints`), yönetici ve temsilci.

Onaylar sekmesiyle **aynı görsel dil**: filtre çipleri (`.filter-chips`), liste + ayrıntı (`.master-detail`, `.md-row`,
`.md-detail`), renk tonları (`tone-*`), bekleme rozeti (`.wait`), karar alanı (`.decision-note`, `.decision-actions`).

- **Filtre çipleri:** Açık · Beklemede · İncelemede · Çözüldü · Reddedildi · Tümü; temsilcide ayrıca "Bana atananlar".
  Sayılar Admin sayfasının rozet için zaten çektiği `Counts` parametresinden gelir — ek istek yok.
- **Liste satırı:** durum tonunda şikayet ikonu, başlık olarak şikayet metninin ilk satırı (2 satıra kadar),
  `#no · Sipariş #no · atanan`, durum rozeti; SLA yalnızca dikkat gerekiyorsa etiketlenir ("SLA yaklaşıyor" /
  "SLA aşıldı"). Liste yüklenince seçili kayıt yoksa ilk şikayet seçilir. 20'li sayfalama.
- **Ayrıntı:** başlıkta durum ve SLA, altında yaş / müşteri / atanan; şikayet metni alıntı olarak; sipariş bilgileri
  tablo; fotoğraflar ([`ApprovalPhotos`]); müşteriye gönderilen açıklama yeşil kutuda; noktalı zaman çizelgesi (iç
  notlar alıntı biçiminde) ve hemen altında "Not ekle" alanı.
- **Alt bölüm (karar alanı):** Üstlen (temsilci) · Temsilciye ata (admin, stilli seçici) · İncelemede iken
  "Müşteriye gönderilecek açıklama" (zorunlu) + isteğe bağlı iç not ve Reddet / Çözüldü · kapalıyken "Yeniden aç"
  (gerekçe alanı açılır).
- **Ayrı alanlar:** müşteriye giden açıklama yalnızca kapanış alanında, iç not ayrı alanlarda yazılır — ortak tek
  kutu metnin yanlış yere kaydedilmesine yol açıyordu.
- Düğmeler sunucunun `actions` listesinden çizilir; etkinlik `ComplaintView.CanSubmit`. Her eylem `expectedVersion`
  gönderir; 409'da kayıt yenilenir ve uyarı gösterilir.
- Admin sayfasının 15 sn'lik yenilemesi `RefreshAsync`'i çağırır; yazılan metinler korunur.

Yardımcılar: `Helpers/ComplaintView.cs` (durum tonu, SLA etiketi, başlık kısaltma, zaman çizelgesi tonu, yaş, rozet,
alan kuralları) — `ComplaintViewTests`.
