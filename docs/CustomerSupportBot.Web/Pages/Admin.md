# Admin.razor

## Ne İşe Yarar
Admin ve Agent panelinin ana sayfasıdır. Approvals, escalations, aktif chat oturumları, analytics dashboard, improvement yönetimi ve agent listesini sekme tabanlı arayüzde sunar.

## Hangi Amaçla Kullanılır
`/admin` route'unda, `Admin` veya `Agent` rolüyle erişilir. Tüm yönetim işlemlerinin tek noktadan yapıldığı kapsamlı dashboard'dur.

## Sorumlulukları
- Sekme navigasyonu: Approvals, Escalations, Chat Sessions, Analytics, Improvements, Agents, Sessions.
- Approval onay/red işlemleri (gerekçe zorunluluğu dahil). Onay Kuyruğu sekmesindeki kayıt, admin
  karar verene ya da çok uzun süre (varsayılan 72 saat, `ApprovalOptions.StalePendingHours`)
  yanıtsız kalırsa arka planda otomatik reddedilene kadar kuyrukta bekler — sabit bir saniye
  sayacı yoktur (bkz. [`SlaPortService.md`](../../CustomerSupportBot.Application/Sla/SlaPortService.md#slaapprovalsonbreach-varsayılanı--autoreject--none), eskiden burada yanlışlıkla 60 saniyede otomatik reddeden bir SLA config'i vardı).
- Onay kartını çizmek — aşağıdaki bölüme bakın.
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
| **Yüksek risk** rozeti | `ReasonRequired` sunucudan gelir; admin gerekçe zorunluluğunu modal açılmadan **önce** görür |
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

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Tüm yönetim işlemleri tek sayfada toplanmıştır (SPA yaklaşımı). Code-behind pattern'i (`Admin.razor.cs`) kullanılır çünkü sayfa çok büyüktür (~60K+ satır markup + ~28K logic).

## Bağımlılıklar
- [AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [ToastService](../Services/ToastService.md).
- [AdminModels](../Models/AdminModels.md) — Tüm DTO'lar.
