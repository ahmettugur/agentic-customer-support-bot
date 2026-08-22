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
| `private static ComplaintInfo MapToModel(ComplaintEntity e)` | Entity → Domain modeli. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `ILogger<ComplaintRepository>`

## Bağlantılar

- [IComplaintRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)
- [OrderRepository](OrderRepository.md)
