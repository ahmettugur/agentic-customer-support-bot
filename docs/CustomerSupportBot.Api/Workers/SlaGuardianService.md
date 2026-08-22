# SlaGuardianService

- **Dosya:** `Workers/SlaGuardianService.cs`
- **Namespace:** `CustomerSupportBot.Api.Workers`
- **Taban sınıf:** `BackgroundService`

## 1. Ne İşe Yarar

Periyodik olarak (varsayılan aralıkla) bekleyen onay/eskalasyon kayıtlarını tarayıp SLA
(Service Level Agreement) ihlali/uyarısı üreten arka plan servisi. Gerçek tarama mantığı
`ISlaPort.ScanOnceAsync` içindedir (Application katmanı); bu sınıf yalnızca **zamanlama** ve
**çoklu pod'da tek seferlik yürütme garantisi** sağlar.

## 2. Hangi Amaçla Kullanılır

Uygulama açılışında `BackgroundService` olarak başlar ve süreç sonlanana kadar arka planda
çalışır; her `PollIntervalSeconds` saniyede bir taramayı tetikler. Ürettiği olaylar
[SlaEndpoints](../Endpoints/ObservabilityAndTelemetry.md) üzerinden admin paneline sunulur.

## 3. Sorumlulukları

- `SlaOptions.Enabled` `false` ise hiç başlamaz.
- Sonsuz döngüde: `RunScanWithLockAsync` çağırır, sonra `PollIntervalSeconds` kadar bekler.
- **Çoklu pod'da çift tarama önleme:** `IAppDistributedLock` varsa (Redis bağlıysa) her turda
  kısa ömürlü bir kilit (`sla:guardian:scan`) almayı dener; kilidi alamayan pod o turu atlar.
  Redis yoksa (`_distributedLock == null`) kilitsiz doğrudan tarar — tek instance'lık
  ortamlarda (geliştirme, tek pod) davranış bozulmaz.
- Tarama sırasında oluşan istisnaları yutar ve loglar (`catch (Exception ex)`) — **tek bir
  başarısız tur, servisin tamamen durmasına yol açmamalı**; bir sonraki turda tekrar dener.
- **Üstlenmediği:** hangi kayıtların ihlal sayıldığı, eşik hesaplaması, olay üretimi — hepsi
  `ISlaPort.ScanOnceAsync`'in işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISlaPort` (Application, Inbound) — asıl tarama mantığı.
- `IAppDistributedLock?` (Application, Outbound/Locking; `Adapters.Redis` implemente eder) —
  cross-pod kilit; nullable olması Redis'siz çalışmayı da desteklediğini gösterir.
- `SlaOptions` (`IOptionsMonitor<T>`) — `Enabled`, `PollIntervalSeconds`,
  `Approvals.BreachAfterSeconds`, `Escalations.BreachAfterSeconds`; `IOptionsMonitor` kullanımı
  sayesinde yapılandırma **çalışma zamanında** değişse (appsettings reload) bile her turda
  güncel değer okunur.
- [SlaEndpoints](../Endpoints/ObservabilityAndTelemetry.md) — bu servisin ürettiği olayları
  okuyan HTTP yüzeyi.
- `Program.cs` — `AddHostedService<SlaGuardianService>()` ile kaydedilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Kilit TTL'i poll aralığından KISA tutulur** (`PollIntervalSeconds - 1`): bir pod kilidi alıp
  bir şekilde donarsa/çökerse, kilit bir sonraki poll turundan önce kendiliğinden düşer — başka
  bir pod taramaya devam edebilir. Kilit süresiz olsaydı, çöken bir pod SLA taramasını kalıcı
  olarak durdururdu.
- **`IAppDistributedLock?` nullable constructor parametresi:** Redis olmayan (tek pod / yerel
  geliştirme) ortamlarda DI'da bu servis kaydedilmemiş olabilir; sınıf bunu **zorunlu bir
  bağımlılık** olarak değil, **opsiyonel bir geliştirme** olarak modelleyerek her iki ortamda da
  aynı kod yolunu çalıştırır.
- **`BackgroundService` (sürekli döngü) tercih edildi, `IHostedService` (tek seferlik) değil** —
  bu iş doğası gereği periyodiktir (bkz. tersi örnek: [KnowledgeBaseStartupService](KnowledgeBaseIngestor.md),
  o tek seferlik olduğu için `IHostedService` kullanır).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `SlaGuardianService(ISlaPort, IOptionsMonitor<SlaOptions>, ILogger<SlaGuardianService>, IAppDistributedLock? = null)` | Constructor; kilit parametresi opsiyonel. |
| `protected override Task ExecuteAsync(CancellationToken stoppingToken)` | `BackgroundService`'in ana döngüsü — `Enabled` kontrolü, sonsuz tarama+bekleme döngüsü. |
| `RunScanWithLockAsync(SlaOptions, CancellationToken)` *(private)* | Kilit varsa alıp taramayı yapar, yoksa doğrudan tarar. |
| `LockKey` *(private const)* | `"sla:guardian:scan"` — tüm pod'larda aynı kilit anahtarı. |

## 7. Bağımlılıklar

| Bağımlılık | Neden |
|---|---|
| `ISlaPort` | Gerçek tarama mantığını çağırmak için. |
| `IOptionsMonitor<SlaOptions>` | Her turda güncel yapılandırmayı okumak için. |
| `ILogger<SlaGuardianService>` | Başlangıç/hata loglaması. |
| `IAppDistributedLock?` | Çoklu pod'da çift taramayı önlemek için (opsiyonel). |

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [KnowledgeBaseStartupService](KnowledgeBaseIngestor.md)
- [../Endpoints/ObservabilityAndTelemetry](../Endpoints/ObservabilityAndTelemetry.md)
