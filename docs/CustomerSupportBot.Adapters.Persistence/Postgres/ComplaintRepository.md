# ComplaintRepository

**Dosya:** `Postgres/ComplaintRepository.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IComplaintRepository`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)

## 1. Ne İşe Yarar

`catalog.complaints` tablosuna karşı şikayet oluşturma ve sorgulama yapan depo.

## 2. Hangi Amaçla Kullanılır

- `Create(complaint)` — admin onayından sonra şikayet kaydını gerçekten yazan işlem.
- `Get`/`GetByOrder`/`GetByCustomer` — `complaint_status`, `get_all_complaints` gibi tool'ların veri kaynağı.

## 3. Sorumlulukları

- Üstlendiği: şikayet kodu üretimi (Postgres sequence), CRUD-benzeri okuma/yazma, Entity → `ComplaintInfo` map'i.
- Üstlenmediği: onay akışı (bkz. `ApprovalGateService`), sipariş doğrulama (çağıran taraf `orderId`'nin var olduğunu önceden doğrulamış olmalı).

## 4. İlişkiler

- `IComplaintRepository` portunu implemente eder.
- `IDbContextFactory<CustomerSupportDbContext>`, `ILogger<ComplaintRepository>` enjekte edilir.

## 5. Tasarım Yaklaşımı

Şikayet kodu, EF Core identity/auto-increment yerine doğrudan Postgres `catalog.complaint_seq` sequence'inden (`SELECT nextval(...)`) okunuyor — bu, kodun `Create` çağrısı içinde satır eklenmeden ÖNCE bilinmesini sağlıyor (diğer depolardaki gibi "yaz, sonra `Code`'u geri oku" adımına gerek kalmıyor).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `string Create(ComplaintInfo complaint)` | `complaint_seq`'ten yeni kod alır, kaydı yazar, kodu string olarak döner. |
| `ComplaintInfo? Get(string complaintId)` | Tek şikayeti getirir. |
| `IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId)` | Bir siparişe ait tüm şikayetler, kod sırasına göre. |
| `IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId)` | Bir müşterinin tüm şikayetleri, kod sırasına göre. |
| `Task<ComplaintPage> ListAsync(ComplaintQuery)` | Panel listesi: durum (`open` = Beklemede + İncelemede), atanan / atanmamış, temsilci kapsamı (`VisibleToAgentId`: atanmamış + kendisi). Açıklar `created_at` artan, kapalılar `updated_at` azalan (CASE WHEN sıralaması). |
| `Task<ComplaintCounts> CountOpenAsync(agentId)` | Bekleyen, incelemede ve o temsilciye atanan açık şikayet sayıları. |
| `Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(max)` | SLA taraması için açık şikayetler (`ix_complaints_status_created_at`). |
| `Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(code)` | `catalog.complaint_events` — zaman çizelgesi. |
| `Task<ComplaintTransitionOutcome> TryTransitionAsync(transition)` | **Koşullu geçiş:** `WHERE code = @c AND status = @expected AND version = @v` ile `ExecuteUpdate` (durum, atanan, açıklama — `null` ise korunur —, `updated_at`, `version + 1`) ve olay satırı **aynı transaction**'da; execution strategy içinde. Etkilenen satır 0 ise hiçbir şey yazılmaz → `Conflict`. |
| `Task<bool> AddEventAsync(code, evt)` | Durumu değiştirmeden olay (iç not). |
| `private static ComplaintInfo MapToModel(ComplaintEntity e)` | Entity → Domain modeli. |

**Şema (migration `AddComplaintLifecycle`):** `created_at`, `updated_at` (`now()` varsayılanı; eski kayıtlar migration
anını alır), `assigned_agent_id`, `resolution` (≤ 2000), `approval_id`, `version`; `catalog.complaint_events`
(FK, cascade). `Create` artık `created_at` / `approval_id` yazar ve `created` olayını ekler. Migration öncesi kayıtların
olay geçmişi boştur.

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `ILogger<ComplaintRepository>`

## Bağlantılar

- [IComplaintRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)
- [OrderRepository](OrderRepository.md)
