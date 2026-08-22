# ProductCatalogRepository

**Dosya:** `Postgres/ProductCatalogRepository.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IProductCatalogRepository`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.md)

## 1. Ne İşe Yarar

`catalog.products`/`catalog.categories` tablolarına karşı ürün arama/listeleme yapan salt-okunur depo. Ürün/kategori tool'larının (`ProductToolsService` vb.) arkasındaki gerçek veri kaynağıdır.

## 2. Hangi Amaçla Kullanılır

- `FindProduct(productName)` — LLM'in bir ürün adını tam olarak eşleştirmesi gerektiğinde (sipariş verme, stok sorgusu).
- `GetAll()` — ürün kataloğunun tamamını listeleyen tool çağrıları.
- `GetByCategory(category)` — kategoriye göre filtreli ürün listesi; kategori bulunamama ile kategoride hiç ürün olmama durumunu ayırt eder.
- `GetSelectableCategories()` — en az bir ürünü olan kategori adlarının listesi (boş kategoriler LLM'e "seçilebilir" olarak sunulmaz).

## 3. Sorumlulukları

- Üstlendiği: ürün/kategori okuma sorguları, `Category` navigation'ını `Include` ile yükleyip Domain `ProductInfo`/`CategoryProducts` modeline map etmek.
- Üstlenmediği: stok düşümü (bkz. [`StockDeduction`](StockDeduction.md), sipariş akışının bir parçası), ürün ekleme/güncelleme (bu repo salt-okunur).

## 4. İlişkiler

- `IProductCatalogRepository` portunu implemente eder.
- `IDbContextFactory<CustomerSupportDbContext>` ve `ILogger<ProductCatalogRepository>` enjekte edilir.

## 5. Tasarım Yaklaşımı

`GetByCategory`, kategori adı DB'de yoksa `CategoryProducts.NotFound()`, kategori var ama ürünü yoksa boş `CategoryProducts.Found(name, [])` döner — bu ayrım bilinçli: ikisini tek bir "boş liste" ile ifade etmek, çağıran kodun (ve dolayısıyla kullanıcıya gösterilecek hata mesajının) "kategori yok" ile "kategori boş" arasında ayrım yapmasını imkânsız kılardı.

> 🐞 **Ölü kod:** Sınıfın içinde `ExecuteInTransaction<T>` adlı private, kullanılmayan bir metot var — retry-stratejisi uyumlu transaction çalıştırma deseni için yazılmış (bkz. [`OrderRepository`](OrderRepository.md)'deki canlı örnek) ama bu sınıfın hiçbir public metodu onu çağırmıyor; muhtemelen gelecekte planlanan bir yazma-metodu için hazırlık olarak kalmış. Kaldırılması bu dokümantasyon görevinin kapsamı dışında.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ProductInfo? FindProduct(string productName)` | Tam isim eşleşmesiyle tek ürün arar, kategori adıyla birlikte döner. |
| `IReadOnlyList<ProductInfo> GetAll()` | Tüm ürünleri isme göre sıralı döner. |
| `CategoryProducts GetByCategory(string category)` | Kategori adına göre filtreli ürün listesi; "kategori yok" / "kategori boş" ayrımını korur. |
| `IReadOnlyList<string> GetSelectableCategories()` | En az bir ürünü olan kategori adları. |
| `private T ExecuteInTransaction<T>(...)` | Kullanılmayan retry-uyumlu transaction yardımcı metodu (bkz. yukarı). |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `ILogger<ProductCatalogRepository>`

## Bağlantılar

- [IProductCatalogRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.md)
- [OrderRepository](OrderRepository.md) — aynı retry/transaction deseninin canlı kullanımı
