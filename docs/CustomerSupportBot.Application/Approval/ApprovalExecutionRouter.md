# ApprovalExecutionRouter

**Dosya:** `Services/Approval/ApprovalExecutionRouter.cs`  
**Implements:** `IApprovalExecutionRouter`

## 1. Ne İşe Yarar

HITL onay sonrası **tool yönlendirmesi** yapar. `ApprovalRequest.ToolName`'e göre ilgili `ICustomerSupportToolsService` metotunu çağırır.

## 2. Hangi Amaçla Kullanılır

Admin bir onay isteğini onayladığında, bu router tool'u gerçekten yürütür. Parameters dictionary'sinden parametreleri çıkarır ve doğru tool'a yönlendirir.

> 💡 **Analiz notu:** Bir sekreter gibi — "sipariş oluştur" onayını aldığında bilgiyi ilgili departmana (OrderToolsService) yönlendirir.

> ⚠️ **JSON round-trip sorunu:** Parameters Postgres'ten hydrate edildiğinde değerler `JsonElement` olarak gelir — `GetString`/`GetInt` helper'ları her iki kaynağı da (canlı obje veya JsonElement) doğru okur.

## 3. Metotlar

| Metot | Açıklama |
|-------|----------|
| `ExecuteAsync(request, ct)` | ToolName'e göre ilgili tool metotunu çağırır |

## Bağlantılar

- [../ChatPortService.md](../ChatPortService.md) — Onay akışının başladığı yer
- [../../CustomerSupportBot.Domain/Model/ApprovalRequest.md](../../CustomerSupportBot.Domain/Model/ApprovalRequest.md) — Onay kaydı
