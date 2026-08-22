# OrderRepository

**Dosya:** `Postgres/OrderRepository.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IOrderRepository`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.md)

## 1. Ne İşe Yarar

Sipariş yaşam döngüsünün (oluşturma, sorgulama, iptal, iade talebi) `catalog.orders`/`catalog.order_details` tablolarına karşı tam implementasyonu. Sipariş yazma yollarının ikisi de (`Create`, `PlaceOrder`) çok adımlı, transaction korumalı işlemlerdir.

## 2. Hangi Amaçla Kullanılır

- `PlaceOrder(order)` — asıl sipariş verme yolu: stok düşer + sipariş yazılır, tek transaction.
- `Create(order)` — stok kontrolü yapmadan doğrudan sipariş yazan daha basit yol (demo/seed senaryoları veya stok kontrolünün başka yerde yapıldığı çağrılar için).
- `Get`/`GetByCustomer`/`GetLast` — tool çağrılarının (`order_status`, `get_all_orders`, `get_last_order`) arkasındaki veri kaynağı.
- `Cancel`/`RequestReturn` — sipariş iptali ve iade talebi tool'larının (admin onayından SONRA çalıştırılan) gerçek yazma işlemleri.

## 3. Sorumlulukları

- Üstlendiği: sipariş+satır yazımını tek transaction'da tutmak, stok düşümünü ([`StockDeduction`](StockDeduction.md)) aynı transaction'a dahil etmek, durum geçişlerini (`Processing`/`Shipped` → `Cancelled`, `Delivered` → `ReturnRequested`) iş kurallarıyla (14 günlük iade penceresi gibi) korumak.
- Üstlenmediği: admin onayı bekleme (bu, `ApprovalGateService`/`IApprovalQueue`'nun işi — bu repo yalnızca onaylanmış işlemi yazar), stok düşümünün kendisi (bkz. [`StockDeduction`](StockDeduction.md)).

## 4. İlişkiler

- `IOrderRepository` portunu implemente eder.
- [`StockDeduction.TryDeduct`](StockDeduction.md) ile aynı transaction'ı paylaşır.
- `IDbContextFactory<CustomerSupportDbContext>`, `ILogger<OrderRepository>` enjekte edilir.
- `WellKnown.OrderStatuses` sabitlerini kullanır (bkz. [WellKnown](../../CustomerSupportBot.Domain/WellKnown.md)).

## 5. Tasarım Yaklaşımı

> 🐞 **Neden `Create`/`PlaceOrder` iki `SaveChanges` çağırıp ikisini tek transaction'a alıyor:** Sipariş kodu (`Code`) veritabanı tarafından üretilir (sequence/identity); satırların yabancı anahtarı bu kod olduğundan önce başlık yazılıp kod geri okunmalı (`SaveChanges()`), sonra satırlar eklenebilir. Bu iki adım arasına düşen herhangi bir hata "satırsız hayalet sipariş" bırakırdı — bu yüzden ikisi aynı transaction'a alınır.

> 🐞 **Neden `CreateExecutionStrategy().Execute(...)` içinde çalışır:** Üretimde `EnableRetryOnFailure` açık (bkz. [`PersistenceServiceCollectionExtensions`](../EfCore/PersistenceServiceCollectionExtensions.md)), yani aktif strateji `NpgsqlRetryingExecutionStrategy`'dir ve bu strateji **elle açılmış transaction'ları reddeder** — geçici bir hatada yalnızca tek bir komutu yeniden denemek, çok komutlu bir transaction'ı yarıda bırakabileceğinden güvenli değildir. Çözüm: transaction'ın tamamını stratejiye tek bir yeniden-denenebilir birim olarak vermek. `DbContext` de delegate'in İÇİNDE açılır — yeniden denemede taze bir change-tracker gerekir, aksi hâlde ilk denemede eklenen entity'ler ikinci denemede tekrar yazılırdı.

> 🐞 **`MapToModel` geçmiş regresyonu:** Yorum satırında not düşülmüş — eskiden `Details.FirstOrDefault()` kullanılıyordu, ama şema baştan beri çok satırlıydı; tek satıra indirgeyen bu kod ikinci ve sonraki ürünleri sessizce kaybediyordu. Artık tüm satırlar okunuyor.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `string Create(OrderInfo order)` | Stok kontrolü YAPMADAN sipariş+satırları tek transaction'da yazar, sipariş kodunu döner. |
| `OrderPlacementResult PlaceOrder(OrderInfo order)` | Önce [`StockDeduction.TryDeduct`](StockDeduction.md) çalıştırır (yetersizse `OutOfStock` sonucu ve rollback), başarılıysa siparişi aynı transaction'da yazar. |
| `OrderInfo? Get(string orderId)` | Tek siparişi tüm satırlarıyla getirir. |
| `IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId)` | Müşterinin tüm siparişlerini tarihe göre azalan sıralar. |
| `(string OrderId, OrderInfo Order)? GetLast(string customerId)` | Müşterinin en son siparişi. |
| `bool Cancel(string orderId, string reason)` | Yalnızca `Processing`/`Shipped` durumundaki siparişi `Cancelled` yapar; aksi durum `false` döner (sessiz red — çağıran karar verir). |
| `bool RequestReturn(string orderId, string reason)` | Yalnızca `Delivered` ve 14 günden eski olmayan siparişler için `ReturnRequested` durumuna geçirir. |
| `private static OrderInfo MapToModel(OrderEntity e)` | Entity → Domain modeli, tüm satırları ürün adına göre sıralı map eder. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `ILogger<OrderRepository>`

## Bağlantılar

- [IOrderRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.md)
- [StockDeduction](StockDeduction.md)
- [ProductCatalogRepository](ProductCatalogRepository.md) — aynı retry/transaction deseni
