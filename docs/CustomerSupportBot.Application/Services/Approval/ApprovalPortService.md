# ApprovalPortService

**Dosya:** `Services/Approval/ApprovalPortService.cs`
**Port:** `IApprovalPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Approval`

## 1. Ne İşe Yarar

Admin panelinin (Api katmanındaki `AgentPanelEndpoints`) HITL onay kuyruğuyla konuştuğu tek
kapı: bekleyen/son/"takılı kalmış" onay taleplerini listeler, tekil kayıt getirir, admin
kararını (`onayla`/`reddet`) uygular.

## 2. Hangi Amaçla Kullanılır

Admin panel açıldığında bekleyen onayları (`GetPendingAsync`) veya geçmiş kararları
(`GetRecentAsync`) listelemek için, bir admin bir talebe tıklayıp karar verdiğinde
(`DecideAsync`) çağrılır. `GetStuckExecutionsAsync`, onaylanmış ama
[`ApprovalExecutionRouter`](ApprovalExecutionRouter.md) çalıştırılırken hata almış/yarım
kalmış kayıtları göstermek için kullanılır (admin panelinde bir "dikkat" bölümü besler).

## 3. Sorumlulukları

- **Üstlendiği:** `IApprovalQueue`'yu (kalıcılık) `IApprovalPort` (use case) sözleşmesine
  bağlamak; sonuçları admin panelinde göstermeden önce müşteri adıyla zenginleştirmek
  (`EnrichCustomerNamesAsync`); zaten karara bağlanmış bir talebe tekrar karar verilmesini
  engellemek (`DecideAsync` içindeki `Status != Pending` kontrolü — bu, kuyruk tarafındaki
  atomik "koşullu sahiplenme" güvencesinin ÜZERİNE binen ikinci, erken bir kontroldür).
- **Üstlenmediği:** Onay kararının kalıcılığı/atomikliği (bu `IApprovalQueue` implementasyonlarında —
  `PostgresApprovalQueue`/`InMemoryApprovalQueue`), onay sonrası gerçek işin yürütülmesi (bu
  `ApprovalExecutionRouter`'da, `IApprovalQueue.DecideAsync` içinden çağrılır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IApprovalPort` port'unu implemente eder (Ports/Inbound).
- **Inject eder:** `IApprovalQueue` (kalıcı kuyruk), `ICustomerRepository` (ad zenginleştirme
  için toplu sorgu), `ILogger`.
- **Kimin tarafından çağrılır:** Api katmanındaki admin/agent panel endpoint'leri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`EnrichCustomerNamesAsync`, admin panelinde "Müşteri #1027" yerine gerçek adın görünmesi için
onay kayıtlarına isim yazar. Üç tasarım kararı dikkat çekicidir:

1. **Tek toplu sorgu** — kart başına ayrı bir isim sorgusu yerine, adı eksik olan tüm kayıtların
   müşteri kimlikleri toplanıp `ICustomerRepository.GetFullNamesAsync` ile tek seferde çözülür.
   Kart başına sorgu, kuyruk büyüdükçe panel açılışını **doğrusal olarak** yavaşlatırdı.
2. **Cache'teki canlı nesneyi doğrudan değiştirmek** — `IApprovalQueue`'nun döndürdüğü nesneler
   üzerinde `request.CustomerName = name` ataması yapılır; bu paylaşılan (cache'teki) örneği
   değiştirir. Kasıtlı: değer aynı müşteri için hep aynıdır, kalıcılığa yazılmaz (`CustomerName`
   veritabanı sütunu değildir), ve bir sonraki çağrıda tekrar sorgulanmasını engelleyerek fiilen
   bir memoizasyon görevi görür. Müşterinin adı değişirse, kayıt cache'ten düşene veya uygulama
   yeniden başlayana kadar eski ad görünür — kabul edilebilir bir gecikme.
3. **Ad çözülemezse istisna değil sessiz düşüş** — `GetFullNamesAsync` başarısız olursa
   (`catch`), hata loglanır ama kuyruk yine de döner; kartlar sadece müşteri numarasını gösterir.
   Bir görüntüleme kolaylığının başarısızlığı, tüm onay panelinin açılmasını engellememelidir.

`DecideAsync`'teki `Status != Pending` kontrolü bir **erken çıkış optimizasyonudur**, tek
güvence kaynağı değildir — asıl atomiklik garantisi `IApprovalQueue.DecideAsync`
implementasyonunda (Postgres'te `ExecuteUpdateAsync ... WHERE Status = Pending` koşullu
güncellemesiyle) sağlanır; burada erken kontrol sadece gereksiz bir kalıcılık çağrısını
önler (iki admin aynı anda aynı karta tıklarsa, ikincisi bu katmanda hızlıca reddedilir).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetPendingAsync(CancellationToken ct = default): Task<IReadOnlyList<ApprovalRequest>>` | Bekleyen onay taleplerini kalıcı depodan okur, müşteri adlarıyla zenginleştirir. |
| `GetRecentAsync(int count = 50, CancellationToken ct = default): Task<IReadOnlyList<ApprovalRequest>>` | Son `count` kararı (onaylanmış/reddedilmiş dahil) döner. |
| `GetStuckExecutionsAsync(CancellationToken ct = default): Task<IReadOnlyList<ApprovalRequest>>` | Onaylanmış ama yürütmesi tamamlanmamış/hata almış kayıtları döner. |
| `Get(string id): ApprovalRequest?` | Tekil kayıt getirir (senkron, cache'ten). |
| `DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default): Task<bool>` | Admin kararını uygular; kayıt yoksa veya zaten karara bağlanmışsa `false` döner. |
| `EnrichCustomerNamesAsync(...)` *(private)* | Adı eksik kayıtları toplu sorguyla doldurur, hata durumunda sessizce loglar. |

## 7. Bağımlılıklar (Constructor Injection)

- `IApprovalQueue` — kalıcı onay kuyruğu (Postgres/InMemory implementasyonu).
- `ICustomerRepository` — müşteri adı çözümlemesi için toplu sorgu.
- `ILogger<ApprovalPortService>` — bulunamayan kayıt/zaten karara bağlanmış kayıt/ad çözümleme hatası loglaması.

## Bağlantılar

- [ApprovalExecutionRouter.md](ApprovalExecutionRouter.md) — onay sonrası gerçek işi yürüten taraf
- [ApprovalContextAccessor.md](ApprovalContextAccessor.md) — tur bazlı bağlam taşıyıcı
