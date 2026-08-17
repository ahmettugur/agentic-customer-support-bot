# ApprovalExecutionRouter

**Dosya:** `Services/Approval/ApprovalExecutionRouter.cs`  
**Implements:** `IApprovalExecutionRouter`

## 1. Ne İşe Yarar

HITL onay sonrası **tool yönlendirmesi** yapar. `ApprovalRequest.ToolName`'e göre ilgili `ICustomerSupportToolsService` metotunu çağırır.

## 2. Hangi Amaçla Kullanılır

Admin bir onay isteğini onayladığında, bu router tool'u gerçekten yürütür. Parameters dictionary'sinden parametreleri çıkarır ve doğru tool'a yönlendirir.

> 💡 **Analiz notu:** Bir sekreter gibi — "sipariş oluştur" onayını aldığında bilgiyi ilgili departmana (OrderToolsService) yönlendirir.

> ⚠️ **JSON round-trip sorunu:** Parameters Postgres'ten hydrate edildiğinde değerler `JsonElement` olarak gelir — `GetString`/`GetLines` helper'ları her iki kaynağı da (canlı obje veya JsonElement) doğru okur.

## 3. Metotlar

| Metot | Açıklama |
|-------|----------|
| `ExecuteAsync(request, ct)` | ToolName'e göre ilgili tool metotunu çağırır |

## 4. `GetLines` — çok ürünlü siparişin en kırılgan noktası

`order_placement_tool` artık tek çağrıda birden fazla ürün satırı taşır (bkz. [OrderToolsService](../Tools/OrderToolsService.md)). Bu, `Parameters` sözlüğünde bir **nesne dizisi** demek — diğer tüm parametreler düz string/sayı.

Onay kaydı Postgres'e yazılıp geri okunduğunda bu dizi `JsonElement`'e döner. Okuma sessizce başarısız olursa **admin siparişi onaylar ama hiçbir ürün sipariş edilmez** — kullanıcıya "onaylandı" bildirimi gider, sipariş yoktur. Bu yüzden `GetLines`:

- Hem canlı `OrderLineRequest[]` hem `JsonElement` dizisini okur.
- Alan adlarını **büyük/küçük harfe duyarsız** eşler (sözlük camelCase yazar, `OrderLineRequest`'in özellikleri PascalCase serileşir).
- `quantity`'yi hem sayı hem string olarak kabul eder.
- Okuyamazsa **boş liste** döner — uydurmaz. Boş liste tool tarafında `ValidationError`'a düşer ve sonuç kullanıcıya öyle yansır.

Bu yolun dört senaryosu `ApprovalExecutionRouterTests` ile korunmaktadır (canlı parametre, JSON round-trip, string adet, eksik `lines`).

### Neden `customerId` satırların içinde değil?

`order_cancel_tool`/`return_request_tool` için `customerId`, `Parameters` yerine `ApprovalRequest.CustomerId` alanından okunur — bu, HITL kaydını oluşturan JWT-doğrulanmış kimliğin kanonik alanıdır. `order_placement_tool` ise geçmişten gelen biçimi koruyup `Parameters["customerId"]`'yi kullanır; ikisi de aynı kaynaktan (`IApprovalContextAccessor.Context.CustomerId`) doldurulur.

## Bağlantılar

- [../ChatPortService.md](../Chat/ChatPortService.md) — Onay akışının başladığı yer
- [../../CustomerSupportBot.Domain/Model/ApprovalRequest.md](../../CustomerSupportBot.Domain/Model/ApprovalRequest.md) — Onay kaydı
