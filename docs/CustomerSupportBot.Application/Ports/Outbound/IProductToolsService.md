# IProductToolsService

**Kaynak:** `Ports/Outbound/IProductToolsService.cs`
**Implementasyon:** [`ProductToolsService`](../../Services/Tools/ProductToolsService.md) (bkz. [`ICustomerSupportToolsService`](ICustomerSupportToolsService.md) facade'i)

## 1. Ne İşe Yarar

Ürün sorgulama tool'ları için secondary port: ürün bilgisi ve kategori bazlı ürün listesi —
her ikisi de salt-okunur, onay gerektirmez.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService`, `ProductAgent`'a bu tool'ları sunar; kullanıcı "X ürünü var mı" veya
"Y kategorisinde ne var" dediğinde çağrılır.

## 3. Sorumlulukları

- **Üstlendiği:** Ürün arama ve listeleme sorgularının iş mantığı.
- **Üstlenmediği:** Kalıcılık — [`IProductCatalogRepository`](Persistence/IProductCatalogRepository.md)'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Tools/ProductToolsService` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Sahiplik kontrolü GEREKTİRMEYEN tek tool grubu — ürün kataloğu müşteriye özel değildir, bu
yüzden metot imzalarında `customerId` yoktur (diğer tool port'larından ayıran temel fark).

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ToolResult ProductInquiryTool(string productName)` | Belirli bir ürün hakkında bilgi. |
| `ToolResult ProductListTool(string? category = null)` | Kategoriye göre (veya tümü) ürün listesi. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ToolResult`'a bağımlıdır.
