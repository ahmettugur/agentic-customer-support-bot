# Admin.razor

## Ne İşe Yarar
Admin ve Agent panelinin ana sayfasıdır. Onaylar, eskalasyonlar, canlı sohbetler, analiz, konuşma arama, siparişler, hazır yanıtlar ve iyileştirme önerilerini **adresli bölümler** (`/admin?tab=…`) halinde sunar; gezinme sol kenar çubuğundadır ([StaffSidebar](../Layout/StaffSidebar.md)).

## Hangi Amaçla Kullanılır
`/admin` route'unda, `Admin` veya `Agent` rolüyle erişilir (`[Authorize(Roles = "Admin,Agent")]`).
Tüm yönetim işlemlerinin tek noktadan yapıldığı kapsamlı dashboard'dur.

> 🐞 **Bulundu ve düzeltildi:** Sayfa eskiden rolsüz `[Authorize]` kullanıyordu — teknik
> olarak çalışıyordu (her iki rol de girebiliyordu) ama diğer dört admin sayfasıyla
> (`Traces`, `Replay`, `Sla`, `Knowledge`) aynı desende olduğu için ilk bakışta onlarla
> karıştırılıp yanlışlıkla `Roles = "Admin"`e daraltılabilirdi — ki bu sayfa
> [AdminApiService](../Services/AdminApiService.md)'in rol-duyarlı `/agent/*` prefix'i
> sayesinde `Agent` rolünü de gerçekten destekliyor. Karışıklığı önlemek için rol listesi
> artık açıkça yazılıyor.

> 🐞 **Bulundu ve düzeltildi — durum rozetleri renksizdi (tüm sekmelerde):** `admin.css`
> içinde `.status-tag.approved`/`.rejected`/`.pending`/`.open`/`.acknowledged`/`.resolved`/
> `.dismissed`/`.expired` için tam bir renk seti tanımlıydı (dark mode dahil), ama sayfadaki
> **hiçbir** `<span class="status-tag">` bu durumu ikinci bir CSS class olarak eklemiyordu —
> yalnızca metin içeriği olarak basılıyordu (`@a.Status`/`@e.Status`, backend'den zaten
> `"approved"`/`"open"` gibi camelCase-enum string olarak geliyor). Sonuç: "APPROVED",
> "OPEN" gibi rozetler her yerde düz, renksiz, kalın metin olarak görünüyordu — Onay
> Kuyruğu, Eskalasyonlar, Geçmiş, Analytics detay satırları dahil 6 nokta. Kullanıcının
> fark ettiği yer Geçmiş sekmesiydi ama kapsam tüm sayfaydı. Her noktada
> `class="status-tag @a.Status"` deseni uygulandı. Açık Eskalasyonlar sekmesindeki
> `isAcknowledged ? "status-tag--acknowledged" : ""` özel-durumu da bu vesileyle kaldırıldı
> — `.status-tag.acknowledged` zaten aynı rengi tanımlıyordu, ayrı bir BEM class'ı
> (`status-tag--acknowledged`) sadece bu tek eksik bağlamayı dolaylı yoldan telafi etmek
> için yazılmış, artık gereksiz bir kopyaydı; CSS'ten de silindi.

> 🐞 **Bulundu ve düzeltildi — çift HTML-encode:** Başlık altındaki açıklama metninde
> `&amp;` yazılmıştı (`@("... &amp; ...")` bir C# string interpolasyonu içinde). Razor,
> `@()` içindeki string'i zaten otomatik HTML-encode ettiğinden, kaynaktaki `&amp;`
> ekrana literal **"&amp;"** olarak basılıyordu. Aynı satırlardaki düz HTML işaretlemesinde
> (`@onclick` butonlarının metni gibi, `@()` dışında) `&amp;` doğrudur ve dokunulmadı —
> yalnızca C# string içeriğine yazılan kopyalar `&` olarak düzeltildi.

## Sorumlulukları
- Bölüm yönetimi: açılan bölüm `?tab=` sorgusundan gelir ([`AdminTabs.Normalize`](../Helpers/AdminTabs.md), temsilci yalnızca
  `escalations`/`chats`). Kenar çubuğu ya da geri tuşuyla adres değişince sayfa **yeniden oluşmaz**: `OnParametersSetAsync`
  yalnızca bölümü değiştirir, açık sohbet paneli ve canlı bağlantılar korunur. Sayfa içinden geçişte (ör. devraldıktan sonra
  sohbetlere) `SwitchTabAsync` adresi de günceller. Sayfa başlığı (`SectionTitle`/`SectionDescription`) bölüme göre değişir;
  "Canlı · 15 sn'de bir" anahtarı otomatik yenilemeyi açıp kapatır, yanındaki düğme hemen yeniler.
- Kenar çubuğu rozetleri: `RefreshBadgesAsync` sonrası sayılar [`StaffBadgeState`](../Services/StaffBadgeState.md)'e yazılır.
- Approval onay/red işlemleri (gerekçe zorunluluğu dahil). Onay Kuyruğu sekmesindeki kayıt, admin
  karar verene ya da çok uzun süre (varsayılan 72 saat, `ApprovalOptions.StalePendingHours`)
  yanıtsız kalırsa arka planda otomatik reddedilene kadar kuyrukta bekler — sabit bir saniye
  sayacı yoktur (bkz. [`SlaPortService.md`](../../CustomerSupportBot.Application/Services/Sla/SlaPortService.md#slaapprovalsonbreach-varsayılanı--autoreject--none), eskiden burada yanlışlıkla 60 saniyede otomatik reddeden bir SLA config'i vardı).
- Onaylar bölümü **liste + ayrıntı** düzenindedir: solda en uzun bekleyen en üstte (15 dakikayı aşan bekleme vurgulanır) ve
  tür/yüksek risk filtreleri; sağda seçili talebin ayrıntısı ve karar alanı. Karar notu artık modal yerine ayrıntı panelinin
  altındadır; yüksek riskli talepte not yazılmadan **Onayla** pasiftir. Karar isteği sürerken düğmeler pasif ve
  "İşleniyor…" görünür; karar verilen talep listeden hemen kalkar, sıradaki seçilir (`DecideApprovalAsync`).
- Eskalasyon kartı: tek ana eylem (**Devral ve sohbet et**), **Temsilciye ata** ve "⋯" menüsünde *Asistana yeniden planlat*,
  *Çözüldü olarak işaretle*, *Gereksiz — kapat*. Durum ve asistan adları Türkçe (`EscalationStatusLabel`, `AgentDisplayName`).
- Onay ayrıntısını çizmek — aşağıdaki bölüme bakın.
- Escalation yönetimi (acknowledge, resolve, dismiss, replan).
- Aktif chat oturumlarını izleme, mesaj geçmişi görme, takeover/release, mesaj gönderme.
- Analytics dashboard ve oturum bazlı analitik (sentiment timeline, grafikler).
- Improvement mining, lesson onay/red.
- Agent listesi.
- Oturum listesi.
- Otomatik veri yenileme (polling).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [ToastService](../Services/ToastService.md), `NavigationManager`.
- **Layout**: `AdminLayout` (yan navigasyon barı dahil).
- **Authorization**: `[Authorize(Roles = "Admin, Agent")]`.
- **Code-behind**: `Admin.razor.cs` — C# iş mantığı ayrı dosyada.

## Onay kartı — bilgi hiyerarşisi

Admin'in kararı tek bir soruya dayanır: **“kimin adına, ne yapılacak?”** Kart bu soruyu en üstte
ve insan diliyle yanıtlar; makine kimlikleri katlanmış `<details>` bloğuna iner.

```
🛒  Yeni Sipariş   [Maria Anders #1027]  [YÜKSEK RİSK]        2dk önce
────────────────────────────────────────────────────────────────────
   Meyve Kokteyli                                        3 adet
   Çikolatalı Bisküvi Karışımı                           2 adet
   ────────────────────────────────────────────────────────────
   2 kalem                                     Toplam    5 adet

   “3 tane Meyve Kokteyli 2 tane de Çikolatalı Bisküvi Karışımı”

   ▸ Teknik ayrıntı
────────────────────────────────────────────────────────────────────
   [✓ Onayla]  [✗ Reddet]
```

| Karar | Neden |
|---|---|
| Başlık tool adı değil **eylem** (`order_placement_tool` → *Yeni Sipariş*) | Admin geliştirici değil; tool adı hata ayıklama bilgisidir, karar bilgisi değil |
| **Müşteri adı + numarası** başlıkta | Ad tanınırlık, numara kesinlik verir (aynı adı taşıyan iki müşteri olabilir). Partner beyanı devreye girdiğinde bu alan daha da kritikleşir |
| Sipariş kalemleri **tablo** | Adetler sağa yaslı ve `tabular-nums` ile hizalı; göz tek kolonda aşağı inip karşılaştırabiliyor. Birden fazla kalemde toplam satırı çıkar |
| **Yüksek risk** rozeti | `ReasonRequired` sunucudan gelir; admin gerekçe zorunluluğunu listede ve ayrıntıda karar vermeden **önce** görür |
| Jenerik gerekçe **gizlenir** | *“OrderAgent bu tool'u çağırmak istiyor.”* PlanningAgent rationale üretemediğinde düşülen şablondur — yer kaplar, bilgi taşımaz |
| Tool/kayıt/session/trace ve ham parametreler **katlanır** | Karar için gerekmez, hata ayıklama için bir tık uzaktadır |

### Tool'a göre gövde

| Tool | Gövde |
|---|---|
| `order_placement_tool` | Sipariş kalemleri tablosu (`ApprovalLines`) |
| `order_cancel_tool` / `return_request_tool` / `complaint_registration_tool` | Olgu listesi (`ApprovalFacts`) — sipariş no + sebep/şikayet metni |
| Tanınmayan | Ham anahtar/değer listesi (`ParseJsonFields`) |

Son satır bilinçli bir emniyet ağıdır: Web projesi Domain'e referans **vermez** (WASM bundle'ı
sunucu tiplerini taşımasın diye), yani tool adları burada string sabittir. Sunucuyla senkron
kayarsa sonuç veri kaybı değil, yalnızca daha ham bir görünümdür.

### Müşteri fotoğrafları

`Parameters["attachmentIds"]` varsa kartta "Müşterinin eklediği fotoğraflar" bölümü çizilir
(`Components/ApprovalPhotos.razor`); tıklanan fotoğraf büyür. Uç yetki istediği için fotoğraf
`<img src>` ile doğrudan alınamaz (tarayıcı Bearer başlığı eklemez): `AdminApiService.GetAttachmentDataUrlAsync`
rol önekiyle (`/attachments/{id}` ya da `/agent/attachments/{id}`) indirir ve data URL'e çevirir;
sonuç oturum boyunca önbellekte tutulur (kart her yoklamada yeniden çizilir). Kimlikler metin
parametre listesinde tekrarlanmaz.

### Analitik panosu

Özet kartlarının ikinci satırı: **Yapay Zekâ Çözüm Oranı** (insan dahil olmadan biten görüşmeler / en az bir
mesajı olan görüşmeler), **İnsana Aktarılan Görüşme**, **Görüşme Başı LLM Maliyeti** (ortalama; medyan
etikette) ve **Toplam LLM Maliyeti** (görüşmeye atfedilemeyen kısım ipucunda). Panelin
`AnalyticsDashboard` modeli sunucunun alan adlarıyla birebir aynıdır; eskiden farklı olduğu için onay ve
eskalasyon istatistikleri hiç görünmüyordu (`AnalyticsDashboardContractTests`).

### Müşteri adı nereden gelir?

`ApprovalRequest.CustomerName` **kalıcı değildir**. Liste panele gönderilmeden hemen önce
`ApprovalPortService` tarafından tek bir toplu sorguyla doldurulur
(`ICustomerRepository.GetFullNamesAsync`). Gerekçeler:

- **Okuma anında çözülür** → müşteri adını değiştirdiğinde panel güncel adı gösterir. Kayıt
  anında yazılsaydı eski ad donar ve düzeltmek migration gerektirirdi.
- **Toplu sorgu** → kart başına ayrı sorgu (N+1), kuyruk büyüdükçe panelin açılışını doğrusal
  olarak yavaşlatırdı.
- **Sessiz düşüş** → müşteri silinmişse veya sorgu hata verirse alan `null` kalır ve kart yalnızca
  numarayı gösterir. Onay kuyruğu, müşteri tablosundaki bir arıza yüzünden açılmamazlık etmez.

`ReasonRequired` ile aynı ruh: panelin ihtiyacı olan türetilmiş bilgi sunucuda hesaplanır, panel
kendi kopyasını tutmaz.

## Temsilci asistanı (canlı sohbet paneli)

Panel başlığındaki **🤖 Asistan** butonu, mesajların üstünde bir kart açar
(`AdminApiService.GetAgentAssistAsync` → `…/chat-sessions/{sid}/assist`; rol önekine göre admin ya da
agent ucu). Kart **istek üzerine** yüklenir — LLM çağrısı her panel açılışında değil, butona basıldığında
ve "Yenile" ile yapılır. İçerik: özet ve müşterinin talebi, önerilen yanıt, duygu durumu, müşteri
profili, açık işler (eskalasyon/bekleyen onay), ilgili bilgi tabanı makaleleri.

**"Taslağı kullan"** taslağı yalnızca mesaj kutusuna alır; **göndermez** — temsilci düzenleyip kendisi
gönderir. Başka bir sohbete geçilir ya da sohbet bitirilirse kart sıfırlanır; geç gelen bir yanıt yanlış
sohbete yazılmaz. LLM bölümü üretilemezse kart hatayı gösterir, diğer bölümler yine görünür.

## Siparişler (yalnız yönetici)

**Siparişler** bölümü: sipariş no ile bul (`GET /orders/{id}`); durum, ürünler, kargo/teslim bilgisi.
İşleniyorsa kargo firması + takip no ile **Kargoya verildi**, kargodaysa **Teslim edildi**. Geçişte müşteriye
e-posta gider; aynı durumu tekrar işaretlemek ikinci e-posta göndermez. Geçersiz geçişte (ör. iptal edilmiş
sipariş) sunucunun mesajı mevcut durumla gösterilir.

## LLM harcama kartları (Analytics)

`LlmBudget:Enabled` açıkken Analytics sekmesinde **Bugünkü** ve **Bu Ayki LLM Harcaması** kartları:
`harcama / limit` ve ilerleme çubuğu (%80'de turuncu, %100'de kırmızı). Limit `0` ise "(limit yok)", çubuk
gösterilmez. Değerler `/analytics/dashboard`'daki `DailyLlm*`/`MonthlyLlm*` alanlarından gelir.

## Konuşma arama (yalnız yönetici)

**Konuşmalar** bölümü: mesaj metni, müşteri no, başlangıç/bitiş tarihi, kapanış nedeni (liste
`conversation-closing/options`'tan) ve etiket. Sekme ilk açılışta son konuşmaları listeler; otomatik
yenileme aramayı tekrarlamaz (sonuçlar ve sayfalar sıfırlanmasın). Tarih seçimi tarayıcının yerel gününe
göre UTC anlarına çevrilir (bitiş günü dahil). Sonuç kartında eşleşen kısım `<mark>` ile vurgulanır;
**Görüntüle** mevcut döküm penceresini açar, **Daha fazla** sonraki 25'i ekler.

## Sohbeti bitir (kapanış nedeni ve etiketler)

Canlı sohbet panelindeki **Sohbeti Bitir** artık doğrudan kapatmaz; bir pencere açar
(`…/conversation-closing/options`): kapanış nedeni (zorunluysa `*`; seçilmeden düğme pasif), etiketler
(virgülle; sık kullanılanlar tıklanarak eklenir) ve not. Onay `…/chat-sessions/{sid}/close`'a gider.
Sunucu doğrulamada reddederse pencere açık kalır ve hata gösterilir — sohbet kapanmamıştır. Kapandıysa
panel sıfırlanır; kayıt yazılamadıysa uyarı bildirimi çıkar. Analitik sekmesinde **Kapanış Nedenleri**
ve **Sık Etiketler** kartları.

## Temsilci durumu

**Temsilci:** başlıkta durum seçici (Çevrimiçi / Uzakta / Çevrimdışı; nokta rengi geçerli durumu
gösterir). Panel açılırken `POST /agent/presence/connect` çağrılır — hesap bir temsilci kaydına bağlı
değilse seçici gösterilmez. Panel açıkken 30 sn'de bir kalp atışı gider. Panel kapanırken ayrıca
"çevrimdışı" gönderilmez (başka sekmede açık olabilir); sunucu kalp atışı kesilince zaman aşımıyla
çevrimdışı sayar.

**Yönetici:** Eskalasyonlar sekmesinin üstünde temsilci şeridi (`GET /agents/presence`; önce
çevrimiçi, sonra uzakta, sonra çevrimdışı; yük `aktif/kapasite`). Kimse çevrimiçi değilse yeni
eskalasyonların atanmadan kuyrukta bekleyeceği belirtilir. Atama penceresinde her temsilcinin durumu
seçenek metninde görünür.

## Hazır yanıtlar

**Canlı sohbet:** mesaj kutusunun solundaki **📋** düğmesi, kutunun üstünde aranabilir bir liste açar
(`AdminApiService.GetSavedRepliesAsync` → `…/saved-replies?q=`; rol önekine göre admin ya da agent ucu;
arama 250 ms gecikmeyle sunucuya gider). Seçilen yanıt mesaj kutusuna eklenir — kutu boşsa yerine konur,
doluysa sonuna eklenir — ve **gönderilmez**; temsilci düzenleyip kendisi gönderir. Seçimden sonra liste
kapanır.

**Yönetim:** yalnız yöneticinin gördüğü **Hazır yanıtlar** bölümü: liste, ekle/düzenle/sil. Kısayol
isteğe bağlıdır; küçük harfe çevrilir (Türkçe `I`/`İ` → `i`) ve benzersizdir — çakışmada sunucunun
hata metni formda gösterilir.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Tüm yönetim işlemleri tek sayfada toplanmıştır (SPA yaklaşımı). Code-behind pattern'i (`Admin.razor.cs`) kullanılır çünkü sayfa çok büyüktür (~60K+ satır markup + ~28K logic).

## Bağımlılıklar
- [AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [ToastService](../Services/ToastService.md).
- [AdminModels](../Models/AdminModels.md) — Tüm DTO'lar.
