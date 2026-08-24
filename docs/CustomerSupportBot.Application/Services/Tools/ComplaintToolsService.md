# ComplaintToolsService

- **Kaynak:** `Services/Tools/ComplaintToolsService.cs`
- **Tür:** `public sealed class : IComplaintToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## 1. Ne İşe Yarar

Şikayet yönetimiyle ilgili ajan-çağrılabilir tool'ları barındırır: şikayet kaydı, durum
sorgulama, müşterinin tüm şikayetlerini listeleme.

## 2. Hangi Amaçla Kullanılır

`ComplaintAgent` (Adapters.Agents) ve `ApprovalGateService`'in şikayet işlemlerini
gerçekleştirmesi için.

## 3. Sorumlulukları

**Üstlendiği:** Girdi doğrulama, `orderId` üzerinden şikayetin hangi müşteriye ait olduğunu
türetmek (`customerId` verilmemişse), sahiplik/enumeration koruması, mükerrer şikayet
koruması.

**Üstlenmediği:** HITL onay akışı (`ApprovalGateService`'in işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IComplaintRepository`, `IOrderRepository` — kalıcılık portları (şikayet, ilişkili sipariş).
- [`SideEffectIdempotencyCache`](SideEffectIdempotencyCache.md) — mükerrer koruma.
- `ApprovalGateService` (Adapters.Agents) — `ComplaintRegistrationTool` HITL onayı gerektiren
  bir tool olarak buradan sarılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `customerId` neden opsiyonel ve `orderId`'den türetilebilir

Şikayet kaydı için `customerId` verilmemişse, ilişkili siparişin (`order.CustomerId`) sahibi
otomatik olarak kullanılır (`customerIdInferred: true` ile işaretlenir). `customerId` **verildiyse**
ve siparişin gerçek sahibiyle eşleşmiyorsa çağrı **başkasının siparişine şikayet açma**
girişimi olarak reddedilir.

### `ComplaintNotAccessibleMessage` — [`OrderToolsService`](OrderToolsService.md) ile aynı enumeration-oracle savunması

Şikayet "yok" ile "var ama başkasının" durumları **aynı** metinle (`"'{id}' numaralı şikayet
bulunamadı."`) bildirilir — ayrı metinler dönseydi, dışarıdan şikayet numarası taranarak hangi
numaraların var olduğu (ve dolaylı olarak başka müşterilerin şikayet hacmi) öğrenilebilirdi.
Hata **kodu** farklıdır (`CustomerIdMismatch` vs `ComplaintNotFound`), trace/admin panelinde
gerçek sebep görünür.

### Kullanıcıya giden metinden "sağladığınız müşteri kimliği" ifadesi neden kaldırıldı

`customerId` artık kullanıcının serbestçe "sağladığı" bir şey değil — JWT'den gelir. Eski metin
hem yanıltıcıydı hem de kullanıcıya kendi iç kimlik numarasını gereksiz yere gösteriyordu.

### Mükerrer koruma imzası neden `effectiveCustomerId` (türetilmiş) kullanır, ham parametre değil

İmza `[orderId, effectiveCustomerId, complaintText]` üzerinden kurulur — `customerId`'nin
çağrıda verilip verilmemesi (boş mu, açık mı) aynı şikayeti iki farklı çağrı gibi
göstermemeli; türetilmiş (nihai) değer kullanılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ComplaintRegistrationTool(orderId, complaintText, customerId?)` | `orderId` zorunlu, `complaintText` en az 10 karakter (trim edilmiş); sipariş bulunamazsa/erişilemezse enumeration-oracle-güvenli hata; mükerrer kontrolü; başarılıysa `Pending` statüsünde yeni şikayet. |
| `ComplaintNotAccessibleMessage(complaintId)` *(private static)* | Enumeration oracle'a karşı tekilleştirilmiş "bulunamadı" mesajı. |
| `ComplaintStatusTool(complaintId, customerId)` | Sahiplik kontrolüyle şikayet durumunu döner. |
| `GetAllComplaintsTool(customerId)` | Müşterinin **tüm** şikayetlerini (kapasitesiz) listeler; hiç yoksa `NoComplaintsForCustomer`. |

## 7. Bağımlılıklar

Constructor injection ile: `IComplaintRepository`, `IOrderRepository`,
`SideEffectIdempotencyCache?` (opsiyonel).

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md) — aynı enumeration-oracle ve idempotency desenlerini paylaşan kardeş servis
- [SideEffectIdempotencyCache.md](SideEffectIdempotencyCache.md)
