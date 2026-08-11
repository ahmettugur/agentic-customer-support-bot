# ApprovalContextAccessor

**Dosya:** `Services/Approval/ApprovalContextAccessor.cs`

## 1. Ne İşe Yarar

Tool lambda'ları içinden `SessionId`, `TraceId` gibi bağlam bilgilerine erişimi sağlar. `ApprovalGateService`'in `ApprovalRequest.SessionId` vb. doldurabilmesi için kullanılır.

> 💡 **Analiz notu:** `HttpContext.Items` benzeri bir request-scoped bağlam — tool çağrısı yapıldığında "hangi session'dayız?" bilgisini taşır.

## Bağlantılar

- [ApprovalPortService.md](ApprovalPortService.md) — Bu accessor'ı kullanan servis
