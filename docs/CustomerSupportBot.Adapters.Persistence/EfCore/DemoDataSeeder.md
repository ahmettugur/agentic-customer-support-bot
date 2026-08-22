# DemoDataSeeder

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/DemoDataSeeder.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`DemoDataSeeder`, boş bir PostgreSQL veritabanı ile uygulama ilk kez başlatıldığında; varsayılan kullanıcı hesaplarını (admin, agent, customer), canlı temsilci havuzunu (`HumanAgentEntity`) ve [NorthwindSeedData](NorthwindSeedData.md) içerisindeki e-ticaret kategorilerini, ürünlerini, müşterilerini ve örnek siparişlerini tohumlayan (seed) sınıftır.

## Hangi amaçla kullanılır`?

- Yeni kurulan ortamlarda sıfırdan kullanıcı veya ürün girmeden botu test edilebilir hale getirmek.
- Idempotent çalışma mantığıyla, veritabanında zaten kayıt varsa mükerrer kayıt oluşturmamak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `SeedAsync`
```csharp
public static async Task SeedAsync(
    CustomerSupportDbContext context,
    IPasswordHasher passwordHasher,
    ILogger logger,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Tüm tohumlama adımlarını sırayla işletir.
- **İç Mantığı:**
  1. `context.Users.AnyAsync`: Kullanıcı tablosu boşsa varsayılan admin (`admin`), agent (`agent1`, `agent2`) ve müşteri (`cust1`, `cust2`) hesaplarını hash'lenmiş parolalarla oluşturur.
  2. `context.HumanAgents.AnyAsync`: Temsilci tablosu boşsa 3 adet örnek müşteri temsilcisini (`Mert Yılmaz`, `Zeynep Kaya`, `Can Demir`) kaydeder.
  3. `context.Categories.AnyAsync` ve `context.Products.AnyAsync`: Boşsa [NorthwindSeedData](NorthwindSeedData.md) üzerinden kategorileri ve ürünleri yükler.
  4. `context.Customers.AnyAsync` ve `context.Orders.AnyAsync`: Boşsa örnek müşteri ve sipariş geçmişini yükler.
  5. `context.SaveChangesAsync(ct)` çağrılarak işlem tamamlanır.

## Bağımlılıklar

- [CustomerSupportDbContext](CustomerSupportDbContext.md)
- [NorthwindSeedData](NorthwindSeedData.md)
- `CustomerSupportBot.Application.Ports.Outbound.Auth.IPasswordHasher`
