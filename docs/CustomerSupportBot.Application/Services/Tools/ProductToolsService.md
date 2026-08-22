# ProductToolsService

- **Kaynak:** `CustomerSupportBot.Application/Services/Tools/ProductToolsService.cs`
- **Tür:** `public sealed class : IProductToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## Ne işe yarar?

`ProductToolsService`, <summary> Ürün sorgulama araçları uygulama servisi. </summary> <summary> Fiyatı kullanıcıya gösterilecek biçimde yazar: <c>18,00 TL</c>.  <para> Kültür <b>açıkça</b> tr-TR verilir, ortamın <c>CurrentCulture</c>'ına bırakılmaz — sunucu kültürü ortama göre değişir (container'larda genelde invariant) ve aynı fiyat bir yerde <c>18,00</c>, başka yerde <c>18.00</c> olarak çıkardı.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ProductToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ProductToolsService(IProductCatalogRepository products, IUiHintEmitter uiHint)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ProductInquiryTool`
```csharp
public ToolResult ProductInquiryTool(
        [Description("Sorgulanacak ürünün adı veya kısmi adı")
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ProductListTool`
```csharp
public ToolResult ProductListTool(
        [Description("Filtrelenecek kategori adı (opsiyonel)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IProductToolsService`
