# Sesli Görüşme Ekranı (tam sayfa, küre) — Tasarım

**Tarih:** 2026-10-04 · **Dal:** `feature/voice-call-ui`

## Amaç

Sesli konuşmayı, mesaj kutusundaki bir düğme + altında durum yazısı olmaktan çıkarıp endüstrideki
"görüşme" deneyimine (ChatGPT Voice, Gemini Live) taşımak: tam sayfa koyu ekran, ortada sesle canlanan
bir küre, canlı altyazı, az ve büyük kontroller.

Başarı ölçütleri:
- 🎙'ye basınca tam sayfa görüşme ekranı açılır; durum (bağlanıyor / dinliyor / düşünüyor / konuşuyor /
  sessizde / temsilciye aktarılıyor) kürenin davranışıyla anlaşılır.
- Küre gerçek ses seviyesine tepki verir (dinlerken mikrofon, konuşurken asistan sesi).
- Sessize al, bitir, altyazı aç/kapa; küreye dokunarak asistanın sözü kesilir.
- Görüşme bitince ekran kapanır ve konuşmanın tamamı sohbette balon olarak durur (bugünkü gibi).

## Kapsam

- **Yalnızca Web projesi.** Arka uç (OpenAI Realtime, HITL onay kapısı, canlı altyazı olayları)
  değişmez.
- **Kapsam dışı:** sesle söz kesme (asistan konuşurken mikrofon açık kalması). Bugün istemci,
  hoparlör sesinin mikrofona geri dönmemesi için asistan konuşurken mikrofonu kaynağında kapatıyor
  (`_setState('speaking')` → `track.enabled = false`). Bunu açmak ayrı bir iş; burada söz kesme
  küreye dokunarak yapılır. Küçültme / yan panel / görüntülü görüşme de kapsam dışı.

## Görsel tasarım

**Yerleşim:** her ekran boyutunda tam sayfa (`position: fixed; inset: 0`), koyu radyal zemin.

- **Üst çubuk:** kırmızı kayıt noktası + "Destek Asistanı" + süre (mm:ss); sağda
  "Yapay zekâ ile konuşuyorsunuz". Küçültme yok.
- **Merkez:** küre (masaüstünde ~170px, telefonda ~120px), altında tek-iki kelimelik durum etiketi
  (`aria-live="polite"`), altında olay çipi (varsa).
- **Altyazı:** yalnızca son tur — kullanıcının cümlesi soluk ("Sen: …", canlı yazılır), asistanınki
  belirgin. Eski satırlar birikmez. CC ile kapatılır; tercih `localStorage`'da (erişilemezse açık).
- **Kontroller:** Sessize al (aç/kapa) · **Bitir** (büyük, kırmızı, ortada) · Altyazı (aç/kapa).
  Klavye: `Space` sessize al, `Esc` bitir. Her düğmenin `aria-label`'ı ve `aria-pressed`'i var.

**Kürenin durumları:**

| Durum | Görünüm |
|---|---|
| Bağlanıyor | Soluk, yavaş nefes |
| Dinliyor | Açık mavi/turkuaz; mikrofon seviyesiyle büyür/küçülür |
| Düşünüyor | Yavaş dönen renk geçişi (conic gradient) |
| Konuşuyor | Derin mavi; asistan sesinin seviyesiyle nabız + parlama |
| Sessizde | Gri, sabit, 🔇 rozeti |
| Temsilciye aktarılıyor | Yeşil |
| Hata / izin reddi | Küre yerine kısa açıklama + "Tekrar dene" / "Kapat" |

**Olay çipleri** (sohbet görünmediği için bildirimler burada): tool çalışırken
"🔎 Sipariş durumu sorgulanıyor" (mevcut `TOOL_LABELS`), yan etkili tool sonucunda
"⏳ … onaya gönderildi", `human_handoff_tool` sonrasında yeşil durum. Temsilci katıldığında görüşme
bugünkü gibi kapanır (`__stopVoice`) ve sohbet yazılı devam eder.

**Sesler:** Web Audio ile üretilen kısa tonlar (dosya yok) — bağlanınca yükselen iki nota, bitince
alçalan iki nota. Kısık ses seviyesi.

**Hareket azaltma:** `prefers-reduced-motion: reduce` iken animasyon döngüsü başlamaz; durum yalnızca
renk ve etiketle anlatılır.

## Mimari

```
realtime-client.js ──olaylar──► realtime-ui.js ──JSInvokable──► Chat.razor ──► VoiceCallOverlay.razor
      │ getLevels()                   │ (balonlar: bugünkü gibi)
      └──────────────► voice-orb.js (rAF) ──► #voiceOrb { --level }
```

| Birim | Sorumluluk |
|---|---|
| `Components/VoiceCallOverlay.razor` (yeni) | Ekranın yapısı ve durumu: `Open`, `State`, `StartedAt`, `UserCaption`/`UserCaptionLive`, `AssistantCaption`, `Chip`, `Muted`, `CaptionsOn`, `Error`. Süre saniyede bir yenilenir. Düğmeler `EventCallback` ile Chat.razor'a bildirir. |
| `Chat.razor` | Bileşeni barındırır; JS'ten gelen `VoiceCall*` çağrılarıyla durumu günceller; düğmelerde JS'i çağırır (`__voiceSetMuted`, `__stopVoice`, `__voiceInterrupt`). Klavye kısayolları ekran açıkken. |
| `wwwroot/js/voice-orb.js` (yeni) | `start(getLevels, getState)` / `stop()`. Her karede seviyeyi okuyup yumuşatır (atak hızlı, bırakma yavaş) ve `#voiceOrb`'a `--level` (0–1) yazar. Blazor yeniden çizilmez. Hareket azaltmada başlamaz. |
| `realtime-client.js` | Mikrofon kaynağına ve çalma çıkışına `AnalyserNode` (çalma: kaynaklar → analyser → destination). `getLevels()` → `{ input, output }` RMS 0–1. `setMuted(bool)`: kalıcı sessiz — `_setState` dinlemeye dönerken sessizdeyse mikrofonu açmaz. |
| `realtime-ui.js` | Bugünkü balon davranışı aynen kalır; ek olarak ekranı sürer: açılış/kapanış, durum, altyazı (kullanıcı canlı/son, asistan delta/son), çipler, hata. Sesler burada çalınır. |
| `voice-call.css` (yeni) ya da `styles.css` | Ekran ve küre stilleri; küre boyutu/parlaması `--level`'dan `calc` ile. |
| Kaldırılanlar | Mesaj kutusunun altındaki `#voiceStatus` satırı ve `#voiceInterruptBtn`. 🎙 düğmesi giriş noktası olarak kalır. |

**Söz kesme:** konuşuyor durumunda küreye dokunmak (ve `Enter`, odak kürede iken) `client.interrupt()`
çağırır — mevcut "⏸" düğmesinin yaptığının aynısı.

**Sessize alma:** `setMuted(true)` mikrofon track'ini kapatır; asistan konuşmayı bitirip istemci
dinlemeye döndüğünde de kapalı kalır. Sessizdeyken konuşulamaz, asistan yanıtını tamamlayabilir.

## Hata yönetimi

- Mikrofon izni reddi / başlatma hatası: ekran açık kalır, hata görünümü ("Mikrofon izni reddedildi"
  vb.), "Tekrar dene" yeni bir istemci başlatır, "Kapat" ekranı kapatır.
- Bağlantı koparsa (`close`/`error`): kısa hata mesajı, 2 sn sonra ekran kapanır; balonlar bugünkü gibi
  temizlenir.
- `conversation_ended` (bot görüşmeyi bitirdi / sessizlik zaman aşımı): "Görüşme sonlandırıldı" kısa
  gösterilir, ekran kapanır.

## Test

- Projede Blazor bileşen test aracı yok; yalnızca bu ekran için eklemek orantısız. Doğrulama tarayıcıda,
  sahte `RealtimeClient` ile (canlı altyazıda yapıldığı gibi): her durum, sessize al → dinlemeye geçiş
  (mikrofon kapalı kalmalı), küreye dokunarak söz kesme, altyazı aç/kapa ve tercihin hatırlanması,
  `Esc`/`Space`, izin reddi, hareket azaltma, telefon genişliği, açık/koyu tema.
- `realtime-client.js` sessize alma mantığı ve seviye hesabı tarayıcıda ayrıca denenir.
- Tüm test takımı sonda çalıştırılır (C# tarafı değişmediği için regresyon kontrolü).
