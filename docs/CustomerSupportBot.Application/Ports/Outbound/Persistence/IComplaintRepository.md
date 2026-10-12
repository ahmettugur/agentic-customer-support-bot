# IComplaintRepository

**Kaynak:** `Ports/Outbound/Persistence/IComplaintRepository.cs`
**Implementasyon:** [`ComplaintRepository`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/ComplaintRepository.md)

## 1. Ne İşe Yarar

Şikayet kaydı yönetimi için secondary port: oluşturma, id/sipariş/müşteri bazlı sorgulama.

## 2. Hangi Amaçla Kullanılır

`ComplaintToolsService` şikayet kaydı tool'unun (onaylandıktan sonra
[`IApprovalExecutionRouter`](../IApprovalExecutionRouter.md) tarafından tetiklenir) ve şikayet
durumu sorgulama tool'larının arkasında bu port'u çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Şikayet kaydı CRUD'unun okuma/oluşturma tarafı.
- **Üstlenmediği:** Sahiplik kontrolü — `customerId` doğrulaması çağıran serviste
  (`ComplaintToolsService`) yapılır, bu port ham veri erişimidir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/ComplaintRepository` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Create` bir `string` (yeni şikayet id'si) döner, exception fırlatmaz — çağıran taraf id'yi
doğrudan kullanıcıya/LLM'e döndürebilir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string Create(ComplaintInfo complaint)` | Yeni şikayet oluşturur, id döner. |
| `ComplaintInfo? Get(string complaintId)` | Id ile sorgular. |
| `IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId)` | Siparişe bağlı şikayetler. |
| `IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId)` | Müşterinin tüm şikayetleri. |
| `Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct)` | Panel listesi (durum, atanan, temsilci kapsamı, sayfa). |
| `Task<ComplaintCounts> CountOpenAsync(string? agentId, CancellationToken ct)` | Bekleyen / incelemede / temsilciye atanan sayıları. |
| `Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(int max, CancellationToken ct)` | SLA taraması için açık şikayetler. |
| `Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(string complaintId, CancellationToken ct)` | Zaman çizelgesi. |
| `Task<ComplaintTransitionOutcome> TryTransitionAsync(ComplaintTransition t, CancellationToken ct)` | Koşullu geçiş (`status` + `version`) + olay, tek transaction; kaybeden `Conflict`. |
| `Task<bool> AddEventAsync(string complaintId, ComplaintEvent evt, CancellationToken ct)` | Durumu değiştirmeden iç not. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ComplaintInfo`'ya bağımlıdır.
