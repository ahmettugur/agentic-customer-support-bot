# IProductCatalogRepository

**Kaynak:** `Ports/Outbound/Persistence/IProductCatalogRepository.cs`
**Implementasyon:** [`ProductCatalogRepository`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/ProductCatalogRepository.md)

## 1. Ne İşe Yarar

Ürün kataloğu ve stok yönetimi için secondary port: ürün arama, kategori listeleme, toplu stok
düşümü.

## 2. Hangi Amaçla Kullanılır

`ProductToolsService` (`ProductInquiryTool`, `ProductListTool`) `FindProduct`/`GetAll`/
`GetByCategory`'yi çağırır; sipariş verme akışında [`IOrderRepository.PlaceOrder`](IOrderRepository.md)
bu repository'nin stok düşürme mantığıyla birlikte çalışır.

## 3. Sorumlulukları

- **Üstlendiği:** Ürün arama (tam/fuzzy eşleşme), kategori sorgulama, çok-satırlı **atomik**
  stok düşümü.
- **Üstlenmediği:** Sipariş kaydının kendisi — o [`IOrderRepository`](IOrderRepository.md)'nin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/ProductCatalogRepository` implemente eder; stok düşümü Postgres
tarafında tek transaction içinde yapılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Toplu stok düşümü **ya hep ya hiç**tir: satırlardan biri bile yetmezse hiçbiri düşülmez ve
sonuç (`StockDeductionResult.Shortages`) yetersiz kalan satırları taşır — çağıran taraf tek tek
düşüp elle telafi etmek zorunda kalmaz, atomiklik adaptörün sorumluluğundadır.

`GetSelectableCategories` bilinçli olarak **boş kategorileri dışarıda bırakır**: bu liste
kategori seçim ekranını besler; içi boş bir kategoriyi seçenek olarak göstermek kullanıcıyı
tıkladığında "ürün bulunmamaktadır" ile karşılaşacağı bir çıkmaz sokağa sokar.
`GetByCategory` ise kategorinin hiç bulunamamasıyla bulunup boş olmasını `CategoryProducts`
üzerinden ayırt eder.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ProductInfo? FindProduct(string productName)` | Tam eşleşme veya fuzzy match ile ürün bulur. |
| `IReadOnlyList<ProductInfo> GetAll()` | Tüm ürün listesi. |
| `CategoryProducts GetByCategory(string category)` | Belirli kategorideki ürünler (kategori-yok ile kategori-boş ayrımını korur). |
| `IReadOnlyList<string> GetSelectableCategories()` | En az bir ürünü olan kategori adları. |

> 🐞 **Kaynak koddaki yetim (orphaned) XML doc yorumu:** `IProductCatalogRepository.cs`
> içinde `GetAll()`'dan hemen önce, hiçbir metoda bağlı olmayan bir `<summary>` bloğu var —
> "ya hep ya hiç" toplu stok düşümünü anlatıyor ("Bir siparişin tüm satırlarının stoğunu tek
> seferde düşer... `StockDeductionResult.Shortages`..."). Muhtemelen arayüzden kaldırılmış bir
> `DeductStock`/`TryDeductStock` metodunun yorumu silinirken unutulmuş kalıntısı. Bugün stok
> düşümü [`IOrderRepository.PlaceOrder`](IOrderRepository.md) üzerinden tek transaction'da
> yapılıyor; bu arayüzde ayrı bir stok-düşürme metodu YOK. Kaynak dosyadaki yetim yorumun
> temizlenmesi önerilir — burada yalnızca dokümantasyon amacıyla not düşüldü, kod
> değiştirilmedi.

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ProductInfo`/`CategoryProducts`'a bağımlıdır.
