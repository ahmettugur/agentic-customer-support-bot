# IApprovalQueue

**Kaynak:** `Ports/Outbound/Persistence/IApprovalQueue.cs`
**İmplementasyonlar:** [`PostgresApprovalQueue`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresApprovalQueue.md) (prod), [`InMemoryApprovalQueue`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryApprovalQueue.md) (tek-pod/test)

## 1. Ne İşe Yarar

HITL (human-in-the-loop) onay kuyruğu için secondary port. Onay gerektiren tool çağrılarının
(sipariş verme, iptal, iade, şikayet kaydı) kaydını, karar/yürütme yaşam döngüsünü ve
müşteri bildirimi (görüldü/görülmedi) durumunu yönetir.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` (Adapters.Agents) bir tool onay gerektirdiğinde `CreateAsync` ile kayıt
açar ve **artık admin kararını beklemeden** hemen döner (bloklamayan model). Admin panelinden
karar verildiğinde `DecideAsync` çağrılır; bu, onaylandıysa
[`IApprovalExecutionRouter`](../IApprovalExecutionRouter.md)'ı tetikleyip gerçek işi (sipariş
iptali vb.) karar anında yürütür. Chat tarafı `GetUnseenForSessionAsync`/`MarkSeenAsync` ile
"kaçırılan onay sonucu" bildirimini yönetir.

## 3. Sorumlulukları

- **Üstlendiği:** Onay kaydı CRUD'u, karar verme + yürütme tetikleme, iki farklı okuma modeli
  (hızlı-ama-eksik-olabilir cache / yavaş-ama-kalıcı DB), müşteri bazlı geçmiş ve
  görülme durumu, askıda kalan yürütmelerin tespiti.
- **Üstlenmediği:** Onay kararının GERÇEK işi ne yapacağı — o
  [`IApprovalExecutionRouter`](../IApprovalExecutionRouter.md)'ın işi; bu port yalnızca
  yürütmeyi tetikler ve sonucu saklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresApprovalQueue` (prod, çoklu pod — Redis pub/sub ile senkron) ve
  `InMemoryApprovalQueue` (tek pod) implemente eder.
- `RequestCreated`/`RequestDecided` event'leri `HitlEventPortService`
  (`Application/Services/Escalation`) tarafından dinlenip SSE/chat event'i olarak yayınlanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Cache vs. kalıcı okuma ayrımı bilinçlidir ve kritik bir güvenlik/doğruluk sınırıdır:**

> 🐞 `GetPending()` **süreç-içi cache görünümüdür**. Hızlıdır ama EKSİK olabilir: başka bir
> pod'un oluşturduğu kayıt bu pod'a Redis pub/sub ile ulaşır ve o mesaj kaybolabilir (pub/sub
> en fazla bir kez teslim eder; Redis restart'ı veya ağ kesintisi mesajı düşürür). Cache bir kez
> hydrate olduktan sonra bir daha DB'ye bakmadığı için böyle bir kayıp KALICI olur. Bu yüzden
> yalnızca eksikliğin zararsız olduğu yerde (bu pod'un kendi oluşturduğu kaydın mükerrer-çağrı
> kontrolü) kullanılmalıdır. "Bekleyen işler" listesi, SLA ve süpürme gibi eksikliğin **sessiz
> bir kayba dönüştüğü** her yerde `GetPendingAsync()` (kalıcı, DB'den) kullanılmalıdır.
>
> Aynı ayrım `Get(id)` (cache) ile `GetAsync(id)` (kalıcı) arasında da geçerlidir — sahiplik/
> yetki kontrolü yapan uçlar `GetAsync` kullanmalıdır, aksi hâlde listelenen bir kayıt için
> yapılan işlem 404 dönebilir.

`GetUnseenForSessionAsync`'te `customerId` **zorunlu bir daraltmadır, kolaylık değil**: yalnızca
`sessionId` ile sorgulamak yetmez — onay kayıtları oturumdan bağımsız yaşar (session_id için
yabancı anahtar yoktur), oturum silinse bile kayıt kalır ve sessionId'yi öğrenen başka bir
müşteri bildirimi okuyabilirdi.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default)` | Yeni onay kaydı oluşturur. |
| `Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default)` | Karar verilene kadar bekler (bugün bu 4 tool tarafından kullanılmıyor — bloklamayan modele geçildi, bkz. proje geçmişi). |
| `Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)` | Kararı işler; onaylandıysa `IApprovalExecutionRouter` üzerinden gerçek işi yürütür. |
| `IReadOnlyList<ApprovalRequest> GetPending()` | **Cache** — hızlı, eksik olabilir. |
| `Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)` | **Kalıcı** — yavaş, tam. |
| `IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)` | Son N kayıt (cache). |
| `ApprovalRequest? Get(string id)` | **Cache**'ten tekil okuma. |
| `Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default)` | **Kalıcı** depodan tekil okuma — yetki kontrolü için kullanılmalı. |
| `Task<IReadOnlyList<ApprovalRequest>> GetUnseenForSessionAsync(string sessionId, string customerId, CancellationToken ct = default)` | Bu session'a ait, karara bağlanmış ama müşterinin henüz görmediği kayıtlar. |
| `Task<bool> MarkSeenAsync(string id, CancellationToken ct = default)` | Bildirimi "görüldü" işaretler. |
| `Task<IReadOnlyList<ApprovalRequest>> GetHistoryForCustomerAsync(string customerId, int count = 100, CancellationToken ct = default)` | Müşterinin tüm onay geçmişi (session'dan bağımsız, `customerId` bazlı). |
| `Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default)` | Onaylanmış ama yürütmesi takılıp kalmış (deploy/crash) kayıtlar. |
| `event EventHandler<ApprovalRequest>? RequestCreated` | Yeni kayıt oluştuğunda fırlar. |
| `event EventHandler<ApprovalRequest>? RequestDecided` | Karar verildiğinde (yürütme dahil tamamlandıktan sonra) fırlar. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ApprovalRequest`'e bağımlıdır.
