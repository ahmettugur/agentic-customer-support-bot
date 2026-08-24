# HumanAgentPortService

**Dosya:** `Services/Escalation/HumanAgentPortService.cs`
**Port:** `IHumanAgentPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## 1. Ne İşe Yarar

İnsan temsilci (human agent) kayıtlarının CRUD'unu ve **yük takibini** (kaç açık eskalasyon
bir temsilciye atanmış) yönetir; bir eskalasyonu manuel olarak başka bir temsilciye yeniden
atar (`RerouteEscalation`).

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin panel, temsilci listesini yönetmek (`GetAllMergedAsync`, `CreateAgent`,
`UpdateAgent`, `DeleteAgent`) ve bir eskalasyonu elle başka bir temsilciye devretmek
(`RerouteEscalation`) için bu servisi kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Kayıtlı temsilcileri (`IHumanAgentRegistry`) staff kullanıcı hesaplarıyla
  (JWT `LinkedAgentId`) birleştirmek (`GetAllMergedAsync`); bir eskalasyon çözüldüğünde/kapatıldığında
  atanan temsilcinin yükünü otomatik azaltmak (`_loadTrackingHandler`); manuel yeniden atamada
  yük sayacını doğru güncellemek.
- **Üstlenmediği:** Otomatik (skills-based) routing kararı (bu [`EscalationPolicyService`](EscalationPolicyService.md)'te),
  eskalasyon kaydının kendisi (`IEscalationSink`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IHumanAgentPort` port'unu implemente eder; `IDisposable`'dır (event handler temizliği için).
- **Inject eder:** `IHumanAgentRegistry`, `IEscalationSink`, `ILogger`.
- **Kimin tarafından çağrılır:** Api katmanındaki admin panel (temsilci yönetimi) endpoint'leri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**`GetAllMergedAsync` neden iki kaynağı birleştirir:** Bir temsilci iki şekilde var olabilir —
(a) `IHumanAgentRegistry`'de doğrudan kayıtlı (klasik "insan temsilci" kaydı), (b) bir staff
kullanıcı hesabına (`UserInfo.LinkedAgentId`) bağlı ama henüz ayrı bir `HumanAgent` kaydı
açılmamış. `GetLinkedUsersAsync`, registry'de KARŞILIĞI OLMAYANLARI ekler (`registryIds`
kontrolüyle tekrarı önler) — böylece admin panelinde bir staff kullanıcısı login olur olmaz,
ayrıca elle bir "temsilci" kaydı açmaya gerek kalmadan listede görünür.

> 🐞 **`_loadTrackingHandler` (constructor'da kurulan event aboneliği) — neden otomatik yük
> azaltma gerekliydi.** Bir temsilciye eskalasyon atandığında yükü artırılır
> (`IncrementLoad`, [`EscalationPolicyService`](EscalationPolicyService.md) veya
> [`ChatSessionPortService.TakeOver`](../Chat/ChatSessionPortService.md) tarafından). Bu yükün
> **düşürülmesi**, eskalasyon çözüldüğünde/kapatıldığında (`Resolved`/`Dismissed`) otomatik
> olmalıdır — aksi halde her manuel kapatma noktasında ayrı ayrı `DecrementLoad` çağrılması
> gerekirdi ve biri unutulursa temsilcinin yükü hiç sıfıra dönmeyip, aslında boş olan bir
> temsilci sürekli "meşgul" görünürdü (routing kararlarını olumsuz etkiler — asla o temsilciye
> yeni iş yönlendirilmez). Bu sınıf, `IEscalationSink.RequestDecided` olayına merkezi olarak
> abone olarak bu riski ortadan kaldırır: kapanan HER eskalasyon, kim kapattığına bakılmaksızın
> otomatik olarak yük sayacını düşürür.

`RerouteEscalation`, eski temsilcinin yükünü düşürüp yeni temsilcinin yükünü artırırken
**sıralamaya dikkat eder**: önce eski `SuggestedAgentId`'nin yükü azaltılır, sonra yeni
atama yapılır — böylece aynı temsilciye "yeniden atama" (no-op'a yakın bir senaryo) yük
sayacını bozmaz. `agentId` boş bırakılırsa atama kaldırılır (`newAgent = null`), sadece eski
temsilcinin yükü düşürülür.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetAllMergedAsync(CancellationToken ct = default): Task<IReadOnlyList<HumanAgent>>` | Registry kayıtları + bağlı staff kullanıcılarını (kayıtta olmayanlar) birleştirip ada göre sıralar. |
| `GetAgent(string id): HumanAgent?` | Tekil temsilci getirir. |
| `CreateAgent(HumanAgent agent): HumanAgent` | Yeni temsilci kaydı oluşturur. |
| `UpdateAgent(string id, HumanAgentInput input): HumanAgent?` | Temsilci bilgilerini günceller. |
| `DeleteAgent(string id): bool` | Temsilci kaydını siler. |
| `IncrementLoad(string id): bool` / `DecrementLoad(string id): bool` | Yük sayacını manuel değiştirir. |
| `RerouteEscalation(string escalationId, string? agentId, string? reason): RerouteResult` | Bir eskalasyonu başka temsilciye (veya atamasız duruma) yeniden yönlendirir, yük sayaçlarını günceller. |

## 7. Bağımlılıklar (Constructor Injection)

- `IHumanAgentRegistry` — temsilci kayıtları ve yük sayaçları.
- `IEscalationSink` — eskalasyon kayıtları ve `RequestDecided` olayı (otomatik yük azaltma için).
- `ILogger<HumanAgentPortService>` — otomatik yük azaltmayı debug seviyesinde loglar.

## Bağlantılar

- [EscalationPolicyService.md](EscalationPolicyService.md) — otomatik (skills-based) routing kararı
- [EscalationPortService.md](EscalationPortService.md) — eskalasyon CRUD'u
