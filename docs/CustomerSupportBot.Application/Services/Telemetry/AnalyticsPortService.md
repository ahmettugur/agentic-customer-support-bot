# AnalyticsPortService

- **Kaynak:** `Services/Telemetry/AnalyticsPortService.cs`
- **Tür:** `public sealed class : IAnalyticsPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## 1. Ne İşe Yarar

`IAnalyticsPort` (Inbound port) implementasyonu — admin panelinin analitik/dashboard
görünümünü besleyen agregasyon servisi: oturum sayıları, puanlamalar, onay/eskalasyon
istatistikleri, niyet/faz/sentiment dağılımları ve tek bir oturuma özel derinlemesine analiz.

## 2. Hangi Amaçla Kullanılır

`AnalyticsEndpoints` (Api katmanı) bu servisi `IAnalyticsPort` olarak kullanır — birden fazla
veri kaynağını (rating store, session manager, approval queue, escalation sink) tek bir
çağrıda birleştirip admin paneline hazır DTO'lar üretmek.

## 3. Sorumlulukları

**Üstlendiği:**
- `Rate`/`GetRating`/`GetRecentRatings`/`GetAllRatings` — müşteri memnuniyet puanlarının
  okuma/yazma arayüzü.
- `GetSummaryAsync` — hafif, anonim bir özet nesnesi (dinamik `object`).
- `GetDashboardAsync` — admin panelinin ana dashboard'u için **tüm oturumları tek tek gezip**
  (`GetAsync` her biri için) niyet/faz/sentiment dağılımlarını hesaplayan, daha ağır bir
  agregasyon.
- `GetSessionAnalyticsAsync` — tek bir oturumun derinlemesine analizi (sentiment zaman
  çizelgesi, onay/eskalasyon detayları, toplanan bilgiler).

**Üstlenmediği:** Ham verinin saklanması (her bağımlılık portunun kendi işi); bu servis
yalnızca **birleştirir ve şekillendirir**.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRatingStore` — puanlama CRUD.
- `ISessionManager` — oturum/geçmiş verisi.
- `IApprovalQueue`, `IEscalationSink` — onay/eskalasyon istatistikleri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `GetSummaryAsync` (dinamik `object`) ile `GetDashboardAsync` (`AnalyticsDashboard`) neden ayrı

`GetSummaryAsync` daha eski/hafif bir uç nokta gibi görünüyor — anonim tip döner, alan seti
sabit değil. `GetDashboardAsync` ise güçlü tipli `AnalyticsDashboard` DTO'suna yazar ve
**ekstra olarak** tüm oturumları tek tek gezip sentiment/niyet/faz dağılımlarını hesaplar —
bu, `GetSummaryAsync`'in yapmadığı, daha maliyetli bir iş. İki metodun bir arada durması,
zamanla dashboard'un daha zengin bir görünüme evrildiğini ama eski özet uç noktasının geriye
dönük uyumluluk için (veya farklı bir tüketici için) korunduğunu düşündürür.

### `GetDashboardAsync`'te oturumların tek tek gezilmesi

`allSessions.Select(s => _sessions.GetAsync(s.SessionId, ct))` — niyet/faz/sentiment
dağılımları `SessionInfo` özetinde yok, yalnızca tam `AgentSession.State`'te var; bu yüzden
her oturum için ayrı bir `GetAsync` çağrısı zorunlu. Oturum sayısı büyüdükçe bu metodun
maliyeti doğrusal artar — admin panelinin dashboard'u sık çağrılan bir uç nokta değildir,
bu kabul edilebilir bir trade-off'tur.

### Sentiment alarmı eşiği neden `WellKnown`'dan okunur

`ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative`
— bu, otomatik eskalasyonu tetikleyen **aynı** eşiktir (bkz. Domain/Model/WellKnown). Dashboard
burada "kaç oturum otomatik eskalasyon sınırına yakın/ulaşmış" bilgisini ayrı bir sabit
tanımlamadan, tek doğruluk kaynağından okuyarak gösterir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Rate(sessionId, stars, comment?)` | `stars` `[1,5]` dışındaysa `ArgumentOutOfRangeException`; aksi halde `IRatingStore.Submit` + log. |
| `GetRating(sessionId)` | Bir oturumun puanını döner (yoksa `null`). |
| `GetRecentRatings(count = 20)`, `GetAllRatings()` | Puanlama listeleri. |
| `GetSummaryAsync(ct)` | Oturum/mesaj/puan/onay/eskalasyon/niyet sayılarını tek bir anonim nesnede toplar. |
| `GetDashboardAsync(ct)` | `GetSummaryAsync`'e ek olarak tüm oturumları gezip niyet/faz/sentiment dağılımı, ortalama sentiment skoru, negatif oturum sayısı ve sentiment-alarm sayısını hesaplar; güçlü tipli `AnalyticsDashboard` döner. |
| `GetSessionAnalyticsAsync(sessionId, ct)` | Tek oturum için: temel bilgiler + sentiment zaman çizelgesi (`SentimentHistory`) + puan + o oturuma ait onay/eskalasyon detayları (`ApprovalDetails`/`EscalationDetails`) + `CollectedInfo`. Oturum yoksa `null`. |

## 7. Bağımlılıklar

Constructor injection ile: `IRatingStore`, `ISessionManager`, `IApprovalQueue`,
`IEscalationSink`, `ILogger<AnalyticsPortService>`.
