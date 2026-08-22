# OrderToolsService

- **Kaynak:** `CustomerSupportBot.Application/Services/Tools/OrderToolsService.cs`
- **Tür:** `public sealed class : IOrderToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## Ne işe yarar?

`OrderToolsService`, <summary> Sipariş yönetimi araçları uygulama servisi. </summary> <inheritdoc />

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`OrderToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public OrderToolsService(IOrderRepository orders,
        IProductCatalogRepository products,
        ICustomerRepository customers,
        SideEffectIdempotencyCache? idempotency = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `OrderPlacementTool`
```csharp
public ToolResult OrderPlacementTool(
        [Description("Sipariş satırları — her biri bir ürün adı ve adet")
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ValidateOrderActionable`
```csharp
public ToolResult? ValidateOrderActionable(string orderId, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `OrderStatusTool`
```csharp
public ToolResult OrderStatusTool(
        [Description("Sorgulanacak sipariş numarası (örn: 1030)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetLastOrderTool`
```csharp
public ToolResult GetLastOrderTool(
        [Description("Müşteri kimlik numarası (zorunlu)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllOrdersTool`
```csharp
public ToolResult GetAllOrdersTool(
        [Description("Müşteri kimlik numarası (zorunlu)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `OrderCancelTool`
```csharp
public ToolResult OrderCancelTool(
        [Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ReturnRequestTool`
```csharp
public ToolResult ReturnRequestTool(
        [Description("İade talep edilecek sipariş numarası (zorunlu, ör. '1042')
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IOrderToolsService`
