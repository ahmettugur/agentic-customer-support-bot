# IApprovalPort

**Dosya:** `Ports/Inbound/IApprovalPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

HITL (human-in-the-loop) onay akışı için admin panelinin kullandığı primary port — bekleyen/geçmiş onay isteklerini listeler, admin kararını uygular.

## 2. Hangi amaçla kullanılır?

Api katmanındaki admin approval endpoint'leri, admin panelinde "onay bekleyenler" listesini doldurmak ve admin bir karara (onay/red) tıkladığında bu portun `DecideAsync` metodunu çağırmak için kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Onay isteklerini müşteri adı gibi görüntüleme-dostu alanlarla zenginleştirilmiş olarak sunmak; karar uygulama sözleşmesini tanımlamak.
- **Üstlenmediği:** Onay kararı sonrası gerçek işin (sipariş verme, iade vb.) nasıl yürütüldüğü — bu iş `IApprovalQueue`/`ApprovalGateService` seviyesindedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `IApprovalQueue`'yu (Outbound port) kullanarak admin-dostu view modeli üretir.
- Api katmanındaki admin approval endpoint'leri bu porta bağımlıdır.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`GetPendingAsync`/`GetRecentAsync` metotlarının `async` olmasının nedeni müşteri adlarının veritabanından çözülmesidir — kuyruğun kendisi bellek içi cache'ten gelir ama müşteri adı çözümü DB'ye gider. Bu çözüm **tek bir toplu sorgu** ile yapılır (N+1 sorgu problemi yaşanmaz) — performans açısından bilinçli bir tasarım kararıdır.

`GetStuckExecutionsAsync`, onaylanmış ama yürütmesi tamamlanmamış (askıda kalmış) kayıtları döner — admin panelinin elle müdahale gerektiren durumları göstermesi için vardır; bu sorguda tarih sınırı yoktur.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)` | Bekleyen onay istekleri, `CustomerName` doldurulmuş olarak. |
| `Task<IReadOnlyList<ApprovalRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default)` | Son N onay geçmişi, `CustomerName` doldurulmuş olarak. |
| `Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default)` | Onaylanmış ama yürütmesi askıda kalmış kayıtlar. |
| `ApprovalRequest? Get(string id)` | Tek bir onay isteği. |
| `Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)` | Admin kararını uygular. İstek zaten `Pending` değilse `false` döner (idempotent). |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.ApprovalRequest`.

## Bağlantılar

- [IApprovalQueue](../Outbound/Persistence/IApprovalQueue.md) — implementasyonun dayandığı Outbound port.
