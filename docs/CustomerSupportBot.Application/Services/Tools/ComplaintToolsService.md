# ComplaintToolsService

- **Kaynak:** `CustomerSupportBot.Application/Services/Tools/ComplaintToolsService.cs`
- **Tür:** `public sealed class : IComplaintToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## Ne işe yarar?

`ComplaintToolsService`, <summary> Şikayet yönetimi araçları uygulama servisi. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ComplaintToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ComplaintToolsService(IComplaintRepository complaints,
        IOrderRepository orders,
        SideEffectIdempotencyCache? idempotency = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ComplaintRegistrationTool`
```csharp
public ToolResult ComplaintRegistrationTool(
        [Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu, ör. '1030')
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ComplaintStatusTool`
```csharp
public ToolResult ComplaintStatusTool(
        [Description("Sorgulanacak şikayet numarası (örn: 1001)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllComplaintsTool`
```csharp
public ToolResult GetAllComplaintsTool(
        [Description("Müşteri kimlik numarası (zorunlu)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IComplaintToolsService`
