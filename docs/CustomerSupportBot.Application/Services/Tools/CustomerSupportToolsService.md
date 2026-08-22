# CustomerSupportToolsService

- **Kaynak:** `CustomerSupportBot.Application/Services/Tools/CustomerSupportToolsService.cs`
- **Tür:** `public sealed class : ICustomerSupportToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## Ne işe yarar?

`CustomerSupportToolsService`, Application/Services/Tools/CustomerSupportToolsService.cs ICustomerSupportToolsService facade'ı — sub-service'lere delegate eder. <summary> ICustomerSupportToolsService facade implementasyonu. Tüm çağrıları ProductToolsService, OrderToolsService ve ComplaintToolsService'e iletir. </summary> ─── IProductToolsService ───

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`CustomerSupportToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerSupportToolsService(IProductToolsService product,
        IOrderToolsService order,
        IComplaintToolsService complaint)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ProductInquiryTool`
```csharp
public ToolResult ProductInquiryTool(string productName)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ProductListTool`
```csharp
public ToolResult ProductListTool(string? category = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `OrderPlacementTool`
```csharp
public ToolResult OrderPlacementTool(IReadOnlyList<OrderLineRequest> lines, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ValidateOrderActionable`
```csharp
public ToolResult? ValidateOrderActionable(string orderId, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `OrderStatusTool`
```csharp
public ToolResult OrderStatusTool(string orderId, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetLastOrderTool`
```csharp
public ToolResult GetLastOrderTool(string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllOrdersTool`
```csharp
public ToolResult GetAllOrdersTool(string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `OrderCancelTool`
```csharp
public ToolResult OrderCancelTool(string orderId, string reason, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ReturnRequestTool`
```csharp
public ToolResult ReturnRequestTool(string orderId, string reason, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ComplaintStatusTool`
```csharp
public ToolResult ComplaintStatusTool(string complaintId, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllComplaintsTool`
```csharp
public ToolResult GetAllComplaintsTool(string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ComplaintRegistrationTool`
```csharp
public ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `HumanHandoffTool`
```csharp
public static ToolResult HumanHandoffTool(
        [Description("Kullanıcının temsilciyle görüşme isteme sebebi (1-2 cümle)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ICustomerSupportToolsService`
