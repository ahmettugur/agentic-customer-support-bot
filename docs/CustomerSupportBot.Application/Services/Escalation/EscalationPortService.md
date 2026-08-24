# EscalationPortService

**Dosya:** `Services/Escalation/EscalationPortService.cs`
**Port:** `IEscalationPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## 1. Ne İşe Yarar

Admin panelinin eskalasyon (insan devri talebi) kayıtlarıyla konuştuğu kapı: oluşturma,
listeleme (açık/son/temsilciye özel), tekil getirme, karar uygulama (`onayla`/`reddet`/`kapat`).
Ayrıca `IEscalationSink`'in (driven port) olaylarını `IEscalationPort`'un (driving port)
olaylarına **köprüler**.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki `AdminEndpoints`, eskalasyon listesini göstermek ve admin kararlarını
uygulamak için bu servisi kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** `IEscalationSink` çağrılarını loglayarak `IEscalationPort` sözleşmesine
  bağlamak; `RequestCreated`/`RequestDecided` olaylarını driven'dan driving'e köprülemek.
- **Üstlenmediği:** Eskalasyon adaylığı/dedup/routing kararı (bu [`EscalationPolicyService`](EscalationPolicyService.md)'te),
  kaydın kalıcılığı (`IEscalationSink` implementasyonu).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IEscalationPort` port'unu implemente eder.
- **Inject eder:** `IEscalationSink`, `ILogger`.
- **Kimin tarafından çağrılır:** Api katmanındaki `AdminEndpoints`.
- Yayınladığı `RequestCreated`/`RequestDecided` olayları [`HitlEventPortService`](HitlEventPortService.md)
  tarafından DEĞİL, doğrudan `IEscalationSink`'in olaylarına abone olunarak dinlenir (bkz. §5).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden bir "event bridge" (driven → driving)?** Hexagonal mimaride driven port'lar
(`IEscalationSink`) altyapıya, driving port'lar (`IEscalationPort`) use case'e bakar. Bu servis
constructor'ında `_escalations.RequestCreated += (_, req) => RequestCreated?.Invoke(this, req);`
gibi bir köprü kurar — böylece `IEscalationPort`'u dinleyen taraflar (ör. SignalR/SSE
adaptörleri), altyapı katmanının somut olay mekanizmasına DEĞİL, use-case seviyesindeki soyut
olaya bağımlı olur. Bu, Adapters katmanının Application katmanına sızmasını önler.

> Not: [`HitlEventPortService`](HitlEventPortService.md), sohbet akışına canlı olay yayınlarken
> **doğrudan** `IApprovalQueue`/`IEscalationSink`'in olaylarına abone olur — bu sınıfın
> köprülediği `IEscalationPort.RequestCreated/RequestDecided` olaylarını KULLANMAZ. İkisi paralel
> ama bağımsız iki tüketicidir: biri admin panelinin genel listeleme/CRUD ihtiyacı, diğeri belirli
> bir oturuma özel canlı akış.

`GetRecentForAgentAsync` filtrelemeyi **kendisi yapmaz**, doğrudan adaptöre (`IEscalationSink`)
devreder — yorumda açıkça belirtilir: cache üzerinde (bellekte) elemek, cache'in kendisi zaten
"son N kayıt" penceresi olduğu için sınırı gerçek anlamda uygulamak yerine sadece ötelemek
olurdu; asıl filtreleme veritabanı sorgusunda yapılmalıdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RequestCreated` (`event EventHandler<EscalationRequest>?`) | Yeni eskalasyon oluşturulduğunda tetiklenir. |
| `RequestDecided` (`event EventHandler<EscalationRequest>?`) | Bir eskalasyona karar verildiğinde tetiklenir. |
| `Create(EscalationRequest request): EscalationRequest` | Yeni eskalasyon kaydı oluşturur, loglar. |
| `GetOpen(): IReadOnlyList<EscalationRequest>` | Açık (bekleyen) eskalasyonları döner. |
| `GetRecent(int count = 50): IReadOnlyList<EscalationRequest>` | Son `count` eskalasyonu döner. |
| `GetRecentForAgentAsync(string agentId, int count = 50, CancellationToken ct = default): Task<IReadOnlyList<EscalationRequest>>` | Belirli bir temsilciye ait son eskalasyonları döner; filtreleme adaptörde yapılır. |
| `Get(string id): EscalationRequest?` | Tekil kayıt getirir. |
| `Decide(string id, string action, string? assignedTo = null, string? resolution = null): bool` | Admin kararını uygular; kayıt yoksa `false`. |

## 7. Bağımlılıklar (Constructor Injection)

- `IEscalationSink` — kalıcı eskalasyon kayıtları ve olayları.
- `ILogger<EscalationPortService>` — oluşturma/karar loglaması.

## Bağlantılar

- [EscalationPolicyService.md](EscalationPolicyService.md) — eskalasyon oluşturma politikası
- [HitlEventPortService.md](HitlEventPortService.md) — sohbet akışına canlı olay yayını
- [HumanAgentPortService.md](HumanAgentPortService.md) — temsilci yönetimi ve yeniden atama
