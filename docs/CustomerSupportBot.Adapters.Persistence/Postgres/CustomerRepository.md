# CustomerRepository

**Dosya:** `Postgres/CustomerRepository.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ICustomerRepository`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.md)

## 1. Ne İşe Yarar

`catalog.customers` tablosuna karşı müşteri kimlik/varlık doğrulaması yapan salt-okunur bir depo. Sipariş/şikayet gibi büyük domain modellerini döndürmez — sadece "bu müşteri var mı", "bu e-posta gerçekten ona mı ait", "adı ne" gibi dar, ucuz sorguları cevaplar.

## 2. Hangi Amaçla Kullanılır

- `Exists(customerId)` — tool çağrılarında (`ApprovalGateService` vb.) LLM'in ürettiği/JWT'den gelen müşteri kimliğinin gerçekten var olduğunu doğrulamak için.
- `IsEmailOwnedByCustomerAsync` — müşteri kimlik doğrulama/eşleştirme akışlarında (örn. müşteri hesabı ile CRM kaydını bağlarken) sahiplik kontrolü.
- `GetFullNameAsync` / `GetFullNamesAsync` — bildirim, admin paneli, transkript gibi ekranlarda müşteri ID'sini okunabilir isme çevirmek için (toplu sürüm N+1 sorgudan kaçınır).

## 3. Sorumlulukları

- Üstlendiği: `Customers` tablosuna karşı basit okuma sorguları, `IDbContextFactory` ile kısa ömürlü `DbContext` açıp kapatmak.
- Üstlenmediği: müşteri oluşturma/güncelleme (bu repo tamamen salt-okunur), sipariş/şikayet ilişkisi (bkz. [`OrderRepository`](OrderRepository.md), [`ComplaintRepository`](ComplaintRepository.md)).

## 4. İlişkiler

- `ICustomerRepository` portunu implemente eder (Application katmanı).
- `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir ([`CustomerSupportDbContext`](../EfCore/CustomerSupportDbContext.md)).
- DI kaydı: [`PersistenceServiceCollectionExtensions`](../EfCore/PersistenceServiceCollectionExtensions.md).

## 5. Tasarım Yaklaşımı

Her metot kendi `DbContext`'ini `_dbFactory.CreateDbContext()`/`CreateDbContextAsync()` ile açar ve `using`/`await using` ile kapatır — `DbContext` thread-safe olmadığından ve bu repo Singleton/Scoped ömür sınırlarının dışında (tool çağrıları içinden) çağrılabildiğinden, paylaşılan bir context yerine "istek başına taze context" deseni kullanılır. Sorgular `AsNoTracking()` ile change-tracker maliyetinden kaçınır (hiçbiri güncelleme yapmaz).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool Exists(long customerId)` | Senkron — `Customers.Any(c => c.Id == customerId)`. |
| `Task<bool> IsEmailOwnedByCustomerAsync(long customerId, string email, CancellationToken ct)` | E-posta boşsa `false`; aksi hâlde kayıtlı e-postayla `Trim()` + `OrdinalIgnoreCase` karşılaştırır. |
| `Task<string?> GetFullNameAsync(long customerId, CancellationToken ct)` | Tek müşterinin adını döner, yoksa `null`. |
| `Task<IReadOnlyDictionary<long, string>> GetFullNamesAsync(IReadOnlyCollection<long> customerIds, CancellationToken ct)` | Toplu isim çözümleme; `Distinct()` ile aynı ID'nin kuyrukta birden fazla geçmesine karşı korunur; boş koleksiyon için sorgu atlanır. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>` (constructor injection).

## Bağlantılar

- [ICustomerRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.md)
- [CustomerSupportDbContext](../EfCore/CustomerSupportDbContext.md)
