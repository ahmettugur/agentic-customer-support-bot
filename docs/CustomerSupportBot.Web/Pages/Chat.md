# Chat.razor

## Ne İşe Yarar
Müşteri destek chat arayüzüdür. Kullanıcının AI asistanla gerçek zamanlı metin konuşması yaptığı ana sayfadır.

## Hangi Amaçla Kullanılır
`/` route'unda, `Customer` rolüyle erişilen varsayılan sayfadır. SSE (Server-Sent Events) üzerinden streaming yanıtlar alır, mesaj gönderir, rating verir ve approval bildirimlerini gösterir.

## Sorumlulukları
- Kullanıcıdan mesaj alıp `/chat` endpoint'ine SSE ile göndermek.
- Streaming yanıtları gerçek zamanlı render etmek (karakter karakter).
- Mesaj geçmişini `localStorage` ve API'den senkronize etmek.
- Markdown rendering (Markdig kütüphanesi ile).
- Human-mode durumunu göstermek (insan temsilci bağlandığında).
- Oturum rating'i (yıldız + feedback) göndermek/göstermek.
- Approval bildirimleri (çan ikonu, panel, görüldü işaretleme, geçmiş).
- Dark/light tema toggle.
- `ui_hint` olaylarını karşılamak — bugün tek tür: kategori seçim kartları (aşağıya bakın).
- Yapay zekâ bildirimini göstermek (aşağıya bakın).

### Yapay zekâ bildirimi — iki katman

| Yer | Class | Ne zaman görünür |
|---|---|---|
| Karşılama ekranı, örnek çiplerin altında | `.welcome-ai-notice` | Yalnızca sohbet boşken (ilk temas) |
| Composer'ın hemen altında | `.input-hint` | Her zaman |

İkisi tekrar değil, tamamlayıcı: karşılama bildirimi kullanıcı **ilk mesajını yazmadan önce**
görülür ve yan etkili işlemleri (sipariş/iptal/iade) açıkça sayar; composer altındaki satır
sohbet sürerken kalıcı hatırlatma olarak durur.

Metinlerin ortak noktası, yalnızca "yapay zekâ kullanılıyor" demekle yetinmeyip
**yanılabilirliği** ve **doğrulama çağrısını** söylemeleridir — eski metin
(*"Yapay zeka destekli yanıtlar bilgilendirme amaçlıdır."*) bunların ikisini de içermiyordu,
oysa asıl bildirilmesi gereken bunlar. Bu bot sipariş verebildiği, iptal ve iade
başlatabildiği için doğrulama çağrısı kozmetik değil.

Görsel ton kasıtlı olarak **bilgilendirici, uyarı değil**: nötr yüzey (`--color-surface-alt`)
ve ikincil metin rengi kullanılır; kırmızı/sarı uyarı tonu kullanılmaz.

### Kategori seçim kartları — veri erken gelir, gösterim ertelenir

`category_picker` ipucu **tur ortasında** yayınlanır: `ProductListTool` tool olarak çalışırken
`IUiHintEmitter.Emit`'i çağırır, `WorkflowRunner` da kuyruğu olay döngüsünün içinde hemen
boşaltır. Yani kategori listesi, `ResponseAgent` daha metnini üretmeden tarayıcıya ulaşır.

`OnStreamEvent` bu veriyi geldiği anda `_currentBot.CategoryPicker`'a yazar, ama markup
kartları **turun bitmesini bekler**:

```razor
@if (!msg.IsStreaming && msg.CategoryPicker is { Count: > 0 })
```

> 🐞 **Bulundu ve düzeltildi — çalışırken tıklanabilir arayüz.** Koşulda `!msg.IsStreaming`
> yoktu; kartlar ipucu gelir gelmez çiziliyordu. Ajan çip paneli ise yalnızca akış sürerken
> görünür (`msg.IsStreaming && _agentChips.Count > 0`), dolayısıyla kartların ilk göründüğü
> an ile "ajan hâlâ çalışıyor" görüntüsü **yapısal olarak** çakışıyordu — rastlantı değil,
> sıralamanın garantisi.
>
> Asıl zarar görüntü değildi: o pencerede bir karta tıklamak ikinci bir tur başlatıyordu.
> `SendChipMessage` `SendAsync`'i koşulsuz çağırıyordu (`HandleSendAsync`'teki `_isStreaming`
> kontrolü orada yoktu) ve `__streamChat` önceki akışı iptal etmeyip yalnızca abort
> handle'ının üzerine yazıyor — sonuçta iki eşzamanlı `/chat/stream` isteği aynı
> `_currentBot`'a yazıyor ve ilkini durdurma imkânı kayboluyordu.
>
> İki katmanlı düzeltme: kartlar tur bitmeden çizilmiyor, ayrıca `SendChipMessage` de
> `_isStreaming` kontrolü yapıyor (ileride akış sırasında görünen bir düğme eklenirse diye).

Akışı sonlandıran **her** yol `IsStreaming`'i `false` yapar — `response_complete`, `error`
olayı, `OnStreamComplete` (JS abort dahil) ve `OnStreamError` — dolayısıyla picker gizli
kalamaz. Kullanıcı turu "durdur" ile keserse de kartlar görünür; kategoriler zaten
başarıyla alınmıştır.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IJSRuntime`, [ChatApiService](../Services/ChatApiService.md), `HttpClient`, [AuthTokenStore](../Services/AuthTokenStore.md), `AuthService`, [AppAuthStateProvider](../Services/AppAuthStateProvider.md), `NavigationManager`, [ThemeService](../Services/ThemeService.md).
- **Layout**: `EmptyLayout` (tam ekran chat).
- **Authorization**: `[Authorize(Roles = "Customer")]`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `ChatEndpoints` (SSE stream).

### Token süresi dolması — chat akışı `AuthorizedHttpClientHandler`'ın DIŞINDA

`chat-bridge.js`'teki `__streamChat` (mesaj gönderme, `POST /chat/stream`) ve `_startPersistentEvents` (bildirim kanalı, `EventSource`/`GET /chat/events/{sid}`) ham `fetch`/`EventSource` kullanır — bunlar Blazor `HttpClient` pipeline'ının **dışındadır**, yani [`AuthorizedHttpClientHandler`](../Services/AuthorizedHttpClientHandler.md)'ın 401→refresh→retry mantığı burada devreye girmez. `window._authToken`, sayfa açılışında (`OnAfterRenderAsync` → `__chatSetup`) **bir kez** JS tarafına kopyalanır ve bir daha otomatik güncellenmez.

Bu boşluk gerçek bir kullanıcı sorununa yol açtı: access token sayfa açıkken süresi dolduğunda, `__streamChat`'in `!r.ok` dalı yalnızca `OnStreamError('HTTP 401')` çağırıyor, `_startPersistentEvents`'in `es.onerror`'ı ise **tamamen boştu** (`function () {}`) — kullanıcı "login görünüyorum ama sistem çalışmıyor" durumuna sessizce düşüyordu (bkz. [`AppAuthStateProvider.md`](../Services/AppAuthStateProvider.md) — sayfa açılışındaki proaktif kontrol de aynı sebeple eksikti).

Düzeltme, `AuthorizedHttpClientHandler`'daki mantığın buraya da taşınmasıdır:

| Kanal | JS tarafı | C# tarafı (JSInvokable) |
|---|---|---|
| `__streamChat` (mesaj gönderme) | `r.status===401 && !isRetry` → `OnStreamUnauthorized` çağırır, fetch burada biter | `AuthService.TryRefreshAsync` dener → başarılıysa `__setAuthToken` ile token'ı günceller ve `__streamChat`'i `isRetry=true` ile bir kez tekrar çağırır; başarısızsa `/customer-login`'e yönlendirir |
| `_startPersistentEvents` (`EventSource`) | `es.onerror` bağlantı başına **en fazla bir kez** (`_persistentEventsRetried` bayrağı) `OnPersistentEventsError` çağırır | Aynı refresh mantığı; başarılıysa `_startPersistentEvents(sid, true)` ile yeniden bağlanır (`isAuthRetry=true` → bayrak sıfırlanmaz, ikinci hata tekrar C#'a bildirilmez — sonsuz döngü engellenir) |

`isRetry`/`isAuthRetry` bayrakları olmadan bu mekanizma sonsuz bir JS↔C# döngüsüne girebilirdi: refresh "başarılı" ama sunucu yeni token'ı da reddederse (ör. hesap devre dışı bırakıldıysa), her retry yeni bir 401 üretir ve her 401 yeni bir refresh dener.

### Native sesli mod balonları — Blazor'un render ağacına ham DOM yazılmaz

`#messages` kabı Blazor'un render ağacıdır (`@foreach (var msg in _messages)`). Native sesli mod
(`realtime-ui.js`, `/chat/realtime-native`) eskiden balonları `chat-bridge.js`'teki `chatApp.ui`
üzerinden bu kaba **ham DOM düğümleri** olarak ekliyordu. Blazor bu düğümleri bilmediği için:
"yeni sohbet" `_messages`'ı temizlese de sesli balonlar ekranda kalıyor, sonraki render'larda
Blazor'un kendi düğümleriyle sıraları karışıyordu.

Artık JS yalnızca bir **tutamaç** (`{ id }`) üretir ve değişiklikleri .NET'e iletir; balonlar
`_messages` listesinde yaşar ve Blazor tarafından çizilir:

| JS (`chatApp.ui`) | C# (JSInvokable) | Etki |
|---|---|---|
| `addMessage(role, text, { placeholder, before })` | `VoiceNativeAdd(id, role, text, placeholder, beforeId)` | Balon ekler (`before` verilirse o balonun önüne). |
| `setMessageText(h, text, placeholder)` | `VoiceNativeSetText` | Metni/yer tutucu durumunu günceller. |
| `startStreamingMessage()` / `appendResponseChunk(h, text)` | `VoiceNativeAdd(..., "assistant")` / `VoiceNativeAppend` | Akan asistan balonu. |
| `setAgentStatus(h, label, state)` | `VoiceNativeStatus` | Tool durum etiketi (`.voice-agent-chip`). |
| `finalizeStreamingMessage(h)` / `removeMessage(h)` | `VoiceNativeFinalize` / `VoiceNativeRemove` | Akışı bitirir / boş balonu kaldırır. |

`ChatMsg` bu iş için `VoiceId`, `IsPlaceholder` ve `VoiceStatus` alanlarını taşır. JS → .NET
çağrıları sırayla işlenir. Gerçek mikrofonla uçtan uca test edilmedi; tutamaç köprüsü tarayıcıda
`chatApp.ui` doğrudan çağrılarak doğrulandı.

### Canlı altyazı (kullanıcı balonu)

`realtime-ui.js` kullanıcı balonlarını konuşma kimliğine (`itemId`) göre tutar. `user_transcript_delta`
parçaları geldikçe balon — konuşma bitmeden — yer tutucu stilinde açılır ve metin akar (güncellemeler
~100 ms'lik pencerede toplanıp tek seferde Blazor'a gönderilir). Son transkript (`user_transcript`)
gelince balon birleşik parçalarla değil **son metinle** doldurulur. Hiç metin almamış yer tutucular
yeni bir konuşma başlayınca silinir (gürültü); metni olanlar kendi son transkriptlerini bekler.
Hata/kapanışta metni olan balon canlı stilinden çıkarılır, boş olan silinir. Kimlik taşımayan
olaylarda eski tek yer tutucu davranışı sürer.

### Tek sesli buton

Giriş satırında tek bir mikrofon butonu (`#voiceBtn`) vardır; sesli görüşmeyi
(`/chat/realtime-native`) açar/kapatır. Eskiden yanında ikinci bir "⚡ Hızlı Sesli" butonu ve
"köprü modu" vardı (model yalnızca STT/TTS yapıyor, yanıt ajan hattından geliyordu; JS
`window.App` üzerinden `VoiceTranscript`/`VoiceSendMessage`/`NewChatFromVoice` çağırıyordu).
Köprü modu kaldırılınca bu buton, `window.App` ve ilgili JSInvokable'lar da kaldırıldı.

`realtime-ui.js` sayfa ömrü boyunca bir kez çalışır (`loadScript` aynı betiği yeniden yüklemez), ama
sohbet sayfası uygulama içinde her açılışta `#voiceBtn`'i yeniden çizer. Bu yüzden tıklama belge
düzeyinde dinlenir ve düğme her kullanımda yeniden bulunur — eskiden ilk düğmeye bağlanıldığı için
sohbetten çıkıp dönünce 🎙 çalışmıyordu. Sayfadan çıkarken (`DisposeAsync`) süren görüşme
`__stopVoice` ile bitirilir; yoksa ekran sayfayla birlikte kaybolup mikrofon açık kalırdı.

### Sesli görüşme ekranı

🎙'ye basınca tam sayfa, koyu bir görüşme ekranı (`Components/VoiceCallOverlay.razor`) açılır:
üstte süre ve "Yapay zekâ ile konuşuyorsunuz", ortada sesle canlanan küre ve durum etiketi, altta
son turun altyazısı, en altta Sessize al · Bitir · Altyazı. `Space` sessize alır, `Esc` bitirir;
asistan konuşurken küreye dokunmak sözünü keser.

- **Durum** saf bir modelde tutulur (`Models/VoiceCallModel.cs`, birim testli); `realtime-ui.js` her
  olayı `VoiceCallEvent(type, a, b)` ile iletir. Küre önceliği: hata > temsilciye aktarım > konuşuyor >
  sessiz > bağlanıyor > düşünüyor > dinliyor.
- **Küre** gerçek ses seviyesine tepki verir: `realtime-client.js`'in `AnalyserNode`'ları
  (`getLevels()`), `voice-orb.js` her karede `--level`'ı yazar — Blazor yeniden çizilmez. Hareket
  azaltma tercihinde döngü başlamaz.
- **Sessize alma** istemcide kalıcıdır (`setMuted`): asistan konuşmayı bitirip dinlemeye dönüldüğünde
  de mikrofon kapalı kalır.
- **Altyazı** yalnızca son tur; dökümün tamamı sohbete balon olarak yazılmaya devam eder. Tercih
  `localStorage["csb-voice-captions"]`.
- **Sesle söz kesme yok:** asistan konuşurken istemci yankıyı önlemek için mikrofonu kaynağında kapatır;
  söz kesme küreye dokunarak yapılır.
- **Hatalar:** bağlantı açıkken gelen sunucu hatası (ör. girdi reddedildi) görüşmeyi bitirmez, "⚠ …" çipi
  olarak görünür. Ölümcül hatada istemci **mutlaka durdurulur** (mikrofon serbest kalır): bağlantı hiç
  kurulamadıysa (özellik kapalı, OpenAI'ye ulaşılamadı, yetki) ve mikrofon izni reddinde ekran
  "Tekrar dene" / "Kapat" ile açık kalır; bağlıyken koparsa kısa hata ve 2 sn sonra kapanış. Temsilci
  katılınca (`__stopVoice`) ekran kapanır.
- Bitirilmiş ya da yerine yenisi açılmış bir istemcinin geç olayları yok sayılır; izin penceresi açıkken
  bitirilen görüşme izin verilse de bağlanmaz. Ekran açıkken Tab odağı ekrandaki düğmelerde tutar.
- **Kısayollar** (`Space`/`Esc`/Tab) yalnızca ekran gerçekten görünürken (`.vc-overlay` DOM'da) yakalanır —
  ekran bileşeni kayıtlı değilse tuşlar sayfaya normal gider.
- **Temsilciye aktarım** kalıcı değildir: küre talep oluşturulup duyurulana (ya da müşteri yeniden konuşana)
  kadar "Temsilciye aktarılıyor" gösterir, görüşme sürerse normal durumlara döner; bilgi çipte kalır.
- **Canlı olmayan transkripsiyon modelleri** (`gpt-4o-transcribe` vb.): turun kimliği `speech_stopped` ile
  gelir; yeni tur önceki turun altyazısını kaldırır, eski turun geç gelen transkripti yenisinin üzerine yazılmaz.
- **Bağlantı sesleri** (earcon) için ses bağlamı 🎙 tıklamasında (kullanıcı hareketi içinde) açılır; tarayıcının
  otomatik oynatma kısıtı bağlamı askıya aldıysa her seste devam ettirilir.

### Kişisel verilerim (KVKK)

Başlıktaki kalkan düğmesi küçük bir panel açar: **Verilerimi indir** (`GET /customer/data/export`, tarayıcıda
dosya olarak — `__downloadFile`) ve **Verilerimi sil**. Silme önce neyin silineceğini ve neyin yasal kayıt
olarak kalacağını gösterip onay ister; başarılı olursa ekrandaki sohbet de temizlenir (yeni sohbet). Kısmi
hatada sunucunun mesajı gösterilir ve tekrar denenebilir.

### Fotoğraf ekleme (📎)

- Fotoğraf **seçildiği anda** yüklenir (`ChatApiService.UploadAttachmentAsync`) — görsel analiz,
  kullanıcı mesajını yazarken çalışır; küçük resim "Analiz ediliyor…" ile gösterilir.
- Yüklemeler **sırayla** yapılır: oturum yoksa ilk yükleme onu sunucuda açar ve sayfa bu oturumu
  benimser (`VoiceSetSession` ile aynı yol); paralel olsalar her biri ayrı oturum açardı.
- İstemci sınırları (JPEG/PNG, 5 MB, mesaj başına 3) yalnızca erken geri bildirimdir; asıl kontrol
  sunucudadır.
- Önizlemeden kaldırılan fotoğraf sunucuda da silinir (`DELETE /chat/attachments/{id}`).
- Gönderimde kimlikler `__streamChat`'in son parametresiyle gövdeye (`attachmentIds`) eklenir; 401
  sonrası yeniden denemede de korunur. Yalnızca fotoğraf gönderilirse metin "Fotoğraf ekledim." olur.
  Kullanıcı balonunda fotoğrafların küçük resimleri görünür.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
SSE tercih edilmesinin nedeni tek yönlü streaming için WebSocket'ten daha basit olmasıdır. Mesajlar önce `localStorage`'da tutulur (offline erişim), sonra API ile senkronize edilir. `IAsyncDisposable` uygulanır çünkü SSE bağlantısının temizlenmesi gerekir.

## Bağımlılıklar
- [ChatApiService](../Services/ChatApiService.md) — Rating, approval, session API'leri.
- [AuthTokenStore](../Services/AuthTokenStore.md) — Token okuma (SSE header'ı için).
- [ThemeService](../Services/ThemeService.md) — Tema toggle.
- `Markdig` — Markdown → HTML dönüşümü.
- `IJSRuntime` — Scroll, localStorage, ses efekti vb. JS interop.
