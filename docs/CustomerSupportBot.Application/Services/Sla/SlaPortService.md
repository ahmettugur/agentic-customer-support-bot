# SlaPortService

- **Kaynak:** `Services/Sla/SlaPortService.cs`
- **Tür:** `public sealed class : ISlaPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## 1. Ne İşe Yarar

`ISlaPort` (Inbound/Driving port) implementasyonu — SLA taramasını orkestre eder: bekleyen
onayları ve açık eskalasyonları tek tek [`SlaPolicyEvaluator`](SlaPolicyEvaluator.md)'a verir,
üretilen event'leri kalıcı hale getirir (`ISlaEventSink.Record`) ve breach sonrası aksiyonları
(otomatik red/onay, öncelik yükseltme) **gerçekten uygular**.

## 2. Hangi Amaçla Kullanılır

Bir arka plan işi (`IHostedService`, `SlaOptions.PollIntervalSeconds` periyoduyla) veya admin
panelinin durum sorgusu bu port üzerinden SLA taramasını tetikler / güncel durumu okur.

## 3. Sorumlulukları

**Üstlendiği:**
- `ScanOnceAsync` — tek bir tarama turu: tüm bekleyen onaylar + açık eskalasyonlar için
  `SlaPolicyEvaluator`'ı çağırmak, üretilen event'leri kaydetmek, breach aksiyonlarını
  (`ApplyApprovalBreachAsync`, öncelik yükseltme) uygulamak.
- `GetStatusAsync` — admin paneli için anlık özet durum (`SlaStatusResult`): kaç bekleyen kayıt
  var, en eskisi kaç saniyedir bekliyor, son 200 event içinde kaç breach var.
- `GetRecentEvents` — event geçmişini (1-500 arası, clamp'lenmiş) döndürmek.

**Üstlenmediği:** Yaş/eşik karşılaştırma mantığı ([`SlaPolicyEvaluator`](SlaPolicyEvaluator.md)'ın
işi), periyodik zamanlamanın kendisi (bir `IHostedService`'in işi — bu servis yalnızca "bir
tur tara" der).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISlaEventSink` — event kaydı ve son-yayınlanma sorgusu.
- `IApprovalQueue` — bekleyen onayların listesi + `DecideAsync` (breach sonrası otomatik karar).
- `IEscalationSink` — açık eskalasyonların listesi + önceliğin güncellenmesi.
- `IOptionsMonitor<SlaOptions>` — `GetStatusAsync` her çağrıda **güncel** ayarları okur (canlı
  yeniden yükleme).
- `SlaPolicyEvaluator` — tek kayıt değerlendirme mantığı.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `ScanOnceAsync` neden "kalıcı okuma" yorumuyla açılıyor

Kaynak kodda şu yorum var: *"Kalıcı okuma: SLA'nın göremediği talep için ihlal de üretilmez."*
— yani `_approvals.GetPendingAsync`/`_escalations.GetOpen` neyi dönerse SLA taraması yalnızca
onu görür. Bu, sistemin dayanıklılığıyla (durability) SLA doğruluğu arasındaki bağı açıklar:
eğer altyapı bir onay kaydını kaybederse (teorik olarak), SLA da onu asla ihlal olarak
işaretlemez — çünkü ondan haberi olmaz.

### Breach aksiyonunun uygulanması neden ayrı bir metotta (`ApplyApprovalBreachAsync`)

`SlaPolicyEvaluator.EvaluateApproval` yalnızca **hangi aksiyonun** gerektiğini (`BreachAction`)
söyler, gerçek `IApprovalQueue.DecideAsync` çağrısını yapmaz (saf/yan etkisiz olması gereken bir
değerlendirici). `ApplyApprovalBreachAsync`, bu kararı gerçek bir yan etkiye çevirir —
`decidedBy: WellKnown.Defaults.System` ile, insan değil sistemin karar verdiği açıkça
işaretlenir.

### `GetStatusAsync`'te `IOptionsMonitor.CurrentValue` neden `IOptions.Value` değil

`IOptionsMonitor`, appsettings.json değiştiğinde **yeniden başlatma gerektirmeden** güncel
değeri okur — admin bir eşiği değiştirdiğinde, bir sonraki `GetStatusAsync` çağrısı hemen yeni
değeri yansıtır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetRecentEvents(count = 100)` | `count`'u `[1, 500]` aralığına kırpıp `ISlaEventSink.GetRecent`'e delege eder. |
| `ScanOnceAsync(opts, ct)` | Tüm bekleyen onayları ve açık eskalasyonları sırayla `SlaPolicyEvaluator` ile değerlendirir; üretilen `warn`/`breach` event'lerini kaydeder; onay breach'lerinde `ApplyApprovalBreachAsync`'i, eskalasyon breach'lerinde öncelik güncellemesini uygular. |
| `ApplyApprovalBreachAsync(req, action, ct)` *(private)* | `SlaBreachAction.AutoReject`/`AutoApprove` ise `IApprovalQueue.DecideAsync`'i `decidedBy="system"` ile çağırır; `None` ise hiçbir şey yapmaz. |
| `GetStatusAsync(ct)` | Güncel ayarları (`IOptionsMonitor.CurrentValue`) okuyup bekleyen onay/eskalasyon sayısı, en eski kaydın yaşı, eşikler ve son 200 event içindeki breach sayısını içeren `SlaStatusResult` döner. |

## 7. Bağımlılıklar

Constructor injection ile: `ISlaEventSink`, `IApprovalQueue`, `IEscalationSink`,
`IOptionsMonitor<SlaOptions>`.

## Bağlantılar

- [SlaPolicyEvaluator.md](SlaPolicyEvaluator.md), [SlaOptions.md](SlaOptions.md)
