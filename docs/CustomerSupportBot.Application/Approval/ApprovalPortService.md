# ApprovalPortService

**Dosya:** `Services/Approval/ApprovalPortService.cs`  
**Implements:** `IApprovalPort`

## 1. Ne İşe Yarar

HITL onay operasyonlarını orkestre eder — bekleyen onayları listeleme, onaylama, reddetme. `IApprovalQueue` ile `ApprovalExecutionRouter` arasında köprü.

## 2. Hangi Amaçla Kullanılır

Admin panelinin "Bekleyen Onaylar" sayfası bu servisi kullanır.

## Bağlantılar

- [ApprovalExecutionRouter.md](ApprovalExecutionRouter.md) — Onay sonrası tool yürütme
- [../../CustomerSupportBot.Domain/Model/ApprovalRequest.md](../../CustomerSupportBot.Domain/Model/ApprovalRequest.md) — Onay kaydı
