# IOrderToolsService

**Kaynak:** `Ports/Outbound/IOrderToolsService.cs`
**Implementasyon:** [`OrderToolsService`](../../Services/Tools/OrderToolsService.md) (bkz. [`ICustomerSupportToolsService`](ICustomerSupportToolsService.md) facade'i)

## 1. Ne İşe Yarar

Sipariş yönetimi tool'ları için secondary port: sipariş oluşturma, ön-doğrulama, durum sorgu,
iptal, iade — hepsi LLM'e açık fonksiyonlar olarak.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` bu tool'ları MAF fonksiyonu olarak sarıp `OrderAgent`'a sunar.
`OrderPlacementTool`/`OrderCancelTool`/`ReturnRequestTool` onay akışına girer; diğerleri
salt-okunurdur.

## 3. Sorumlulukları

- **Üstlendiği:** Sipariş satırlarının doğrulanması/katalog çözümlemesi/tekilleştirmesi,
  sahiplik kontrollü sorgu ve yazma tool'ları.
- **Üstlenmediği:** Kalıcılık — [`IOrderRepository`](Persistence/IOrderRepository.md)/
  [`IProductCatalogRepository`](Persistence/IProductCatalogRepository.md)'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Tools/OrderToolsService` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> **`customerId` LLM parametresi DEĞİLDİR** — `OrderStatusTool`, `OrderCancelTool`,
> `ReturnRequestTool` metotlarındaki `customerId`, çağıran taraf (`ApprovalGateService`)
> tarafından HER ZAMAN login'li kullanıcının doğrulanmış kimliğinden ([`IApprovalContextAccessor`](IApprovalContextAccessor.md))
> geçirilir; LLM'in serbest metinden ürettiği bir değer değildir. Sipariş başka bir müşteriye
> aitse `WellKnown.ToolErrorCodes.CustomerIdMismatch` ile reddedilir. Bu, "biri başkasının
> müşteri numarasını söyleyip onun adına işlem yapabiliyor" güvenlik açığının kapatılma
> biçimidir.
>
> `ValidateOrderActionable` ayrı bir metot olarak var çünkü HITL onaylı tool'larda gerçek iş
> admin kararından SONRA çalışır; sahiplik ihlali orada yakalanırsa talep önce admin kuyruğuna
> düşer, admin onaylar ve işlem sessizce başarısız olur. Bu metot aynı kontrolü onay kaydı
> OLUŞTURULMADAN önce yapıp kullanıcıya anında geri bildirim verir ve kuyruğu kirletmez —
> yürütme anındaki kontrolün YERİNE geçmez, durum iki an arasında değişebilir, ikisi birlikte
> çalışır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ToolResult OrderPlacementTool(IReadOnlyList<OrderLineRequest> lines, string customerId)` | Bir/daha fazla satırdan sipariş oluşturur (onay gerektirir); LLM'in ham talebini doğrular/çözümler. |
| `ToolResult? ValidateOrderActionable(string orderId, string customerId)` | Salt-okunur ön kontrol: sipariş var mı, login'li müşteriye ait mi? Engel varsa `ToolResult`, yoksa `null`. |
| `ToolResult OrderStatusTool(string orderId, string customerId)` | Sipariş durumu (sahiplik kontrollü). |
| `ToolResult GetLastOrderTool(string customerId)` | Müşterinin en son siparişi. |
| `ToolResult GetAllOrdersTool(string customerId)` | Müşterinin tüm siparişleri. |
| `ToolResult OrderCancelTool(string orderId, string reason, string customerId)` | Sipariş iptali (onay gerektirir, sahiplik kontrollü). |
| `ToolResult ReturnRequestTool(string orderId, string reason, string customerId)` | İade talebi (onay gerektirir, sahiplik kontrollü). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.OrderLineRequest`/`ToolResult`'a bağımlıdır.
