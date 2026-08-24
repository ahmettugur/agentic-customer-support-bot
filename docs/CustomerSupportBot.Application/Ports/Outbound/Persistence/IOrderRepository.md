# IOrderRepository

**Kaynak:** `Ports/Outbound/Persistence/IOrderRepository.cs`
**Implementasyon:** [`OrderRepository`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/OrderRepository.md)

## 1. Ne İşe Yarar

Sipariş yönetimi için secondary port: oluşturma, atomik sipariş verme (stok düşümü + kayıt),
sorgulama, iptal, iade talebi.

## 2. Hangi Amaçla Kullanılır

`OrderToolsService.OrderPlacementTool` doğrulanmış sipariş satırlarını `PlaceOrder` ile
kalıcılaştırır; sipariş durumu/geçmiş sorguları (`OrderStatusTool`, `GetAllOrdersTool` vb.)
`Get`/`GetByCustomer`/`GetLast`'i çağırır; iptal/iade onaylandığında
[`IApprovalExecutionRouter`](../IApprovalExecutionRouter.md) üzerinden `Cancel`/`RequestReturn`
tetiklenir.

## 3. Sorumlulukları

- **Üstlendiği:** Sipariş CRUD'u ve **atomik** sipariş verme (stok + kayıt tek transaction).
- **Üstlenmediği:** Ürün/stok kataloğu detayı — o [`IProductCatalogRepository`](IProductCatalogRepository.md)'nin işi; `PlaceOrder` onunla birlikte çalışır ama katalog mantığını tekrarlamaz.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/OrderRepository` implemente eder; Postgres tarafında `PlaceOrder`
tek bir DB transaction'ı içinde çalışır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`PlaceOrder`'ın neden tek metot/tek transaction olduğu:** ayrı ayrı yapıldığında (önce
> stok düş, sonra sipariş yaz) ikisinin arasında oluşan herhangi bir hata stoğu düşülmüş ama
> karşılığında hiçbir sipariş oluşmamış hâlde bırakır. Hiçbir yerde hata görünmez; ürün stoğu
> sessizce ve kalıcı olarak azalır. Sipariş verme bölünemez (atomik) bir işlemdir ve bu metot
> onu öyle temsil eder — çağıran taraf ayrı stok-düşürme/sipariş-yazma adımlarını elle
> koordine etmek zorunda kalmaz.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string Create(OrderInfo order)` | Yeni sipariş oluşturur, id döner (stok düşümü içermez — düşük seviye). |
| `OrderPlacementResult PlaceOrder(OrderInfo order)` | Stoğu düşer ve siparişi yazar — tek transaction. |
| `OrderInfo? Get(string orderId)` | Id ile sorgular. |
| `IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId)` | Müşterinin tüm siparişleri. |
| `(string OrderId, OrderInfo Order)? GetLast(string customerId)` | Müşterinin en son siparişi. |
| `bool Cancel(string orderId, string reason)` | Siparişi iptal eder; iptal edilemez durumdaysa `false`. |
| `bool RequestReturn(string orderId, string reason)` | İade talebi oluşturur; uygun değilse `false`. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.OrderInfo`/`OrderPlacementResult`'a bağımlıdır.
