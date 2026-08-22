# PostgreSQL Domain Depoları (Repositories)

- **Kaynaklar:**
  - `CustomerSupportBot.Adapters.Persistence/Postgres/CustomerRepository.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/OrderRepository.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/ProductCatalogRepository.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/ComplaintRepository.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/StockDeduction.cs`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`

## Ne işe yararlar?

Bu sınıflar, Application katmanının e-ticaret domain varlıklarına erişimini sağlayan temel `Driven Adapter` implementasyonlarıdır. `IDbContextFactory<CustomerSupportDbContext>` üzerinden thread-safe kısa ömürlü DbContext örnekleri kullanarak PostgreSQL `catalog` şemasındaki tablolarla haberleşirler.

---

## 1. CustomerRepository
- **Uyguladığı Port:** [ICustomerRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.md)
- **Constructor:** `public CustomerRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Metotlar ve İç Mantık:**
  - `GetByIdAsync(customerId)`: `Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId)` sorgusunu işletip Domain `Customer` nesnesine map eder.
  - `GetAllAsync()`: Tüm aktif müşterileri listeler.

---

## 2. ProductCatalogRepository
- **Uyguladığı Port:** [IProductCatalogRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.md)
- **Constructor:** `public ProductCatalogRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Metotlar ve İç Mantık:**
  - `GetByIdAsync(id)`: Ürünü kategori bilgisiyle birlikte (`Include(p => p.Category)`) getirir.
  - `SearchAsync(name, category, minPrice, maxPrice, inStockOnly)`: Dinamik IQueryable filtreleme yapar; `EF.Functions.ILike` ile büyük/küçük harf duyarsız Türkçe ürün adı araması yapar.

---

## 3. OrderRepository & StockDeduction
- **Uyguladığı Port:** [IOrderRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.md)
- **Constructor:** `public OrderRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Metotlar ve İç Mantık:**
  - `GetByIdAsync(orderId)`: Sipariş ve detaylarını (`Include(o => o.OrderDetails).ThenInclude(d => d.Product)`) getirir.
  - `GetByCustomerIdAsync(customerId)`: Müşteriye ait siparişleri tarihe göre azalan sıralar.
  - `CreateAsync(order)`: **Atomik Stok Düşümü (`StockDeduction.DeductAsync`)** ile birlikte çalışır; `UPDATE catalog.products SET units_in_stock = units_in_stock - @qty WHERE id = @id AND units_in_stock >= @qty` çalıştırılarak yarış durumunda stok eksiye düşerse `InvalidOperationException` fırlatır ve işlemi geri alır (Rollback).
  - `CancelAsync(orderId, reason)`: Sipariş durumunu `Cancelled` yapar ve düşülen stokları geri iade eder.

---

## 4. ComplaintRepository
- **Uyguladığı Port:** [IComplaintRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)
- **Constructor:** `public ComplaintRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Metotlar ve İç Mantık:**
  - `CreateAsync(complaint)`: `catalog.complaints` tablosuna yeni şikayet kaydeder.
  - `GetByCustomerIdAsync(customerId)` ve `GetByOrderIdAsync(orderId)`: İlgili müşterinin veya siparişin şikayet geçmişini sorgular.
