# CategoryProducts

**Dosya:** `Model/CategoryProducts.cs`

## 1. Ne İşe Yarar

`IProductCatalogRepository.GetByCategory` çağrısının sonucu. Kategorinin **bulunup
bulunmadığını**, kanonik adını ve ürünlerini birlikte taşır.

```csharp
public sealed record CategoryProducts(
    bool CategoryExists,
    string? CanonicalName,
    IReadOnlyList<ProductInfo> Products);
```

## 2. Neden ayrı bir tip — boş liste neden yetmiyor

Boş bir ürün listesi iki farklı gerçeği anlatır ve çağıran bunları ayırt edemezse yanlış
davranır:

| Durum | Doğru davranış |
|---|---|
| Katalogda o adda kategori **yok** (ad yanlış) | Geçerli bir adla **tekrar dene** |
| Kategori **var** ama içi boş | Tekrar denemek anlamsız; başka kategori öner |

Eskiden `GetByCategory` her iki durumda da `[]` dönüyordu ve
[`ProductListTool`](../../CustomerSupportBot.Application/Tools/ProductToolsService.md)
ikisini de aynı hata koduyla (`PRODUCT_NOT_FOUND`) ve aynı cümleyle raporluyordu. LLM hangi
durumda olduğunu bilemediği için ya boşuna aynı adla tekrar deniyor ya da pes ediyordu.

Bu, tip sisteminin yakalayamayacağı bir bilgi kaybıydı — derleme hatası vermez, yalnızca
modeli yanlış yönlendirir. Ayrımı taşıyacak bir dönüş tipi tek kalıcı çözüm.

## 3. `CanonicalName`

Kategori adı kolonu `und-u-ks-level1` collation'lı, yani eşleşme case ve aksan duyarsızdır:
`"içecekler"` de `"İçecekler"` kaydını bulur. `CanonicalName` **katalogdaki** yazımı taşır.

Bu olmadan, tool yanıtında çağıranın yazdığı ad (`"içecekler"`) ile ürün satırlarındaki
kanonik ad (`"İçecekler"`) yan yana dönüyordu — tek payload'da iki farklı yazım.

> ⚠️ Turkish-I tuzağı: `"İçecekler".ToLowerInvariant()` **`İ`'yi değiştirmez** (U+0130 olduğu
> gibi kalır), dolayısıyla `.Contains("içecek")` `false` döner. Kategori adlarıyla kültür
> duyarsız string karşılaştırması yapan kod yazarken bunu hesaba katın; eşleşmeyi mümkünse
> veritabanı collation'ına bırakın.

## 4. Fabrika metotları

| Metot | Anlamı |
|-------|--------|
| `CategoryProducts.NotFound()` | Katalogda böyle bir kategori yok. |
| `CategoryProducts.Found(canonicalName, products)` | Kategori var; `products` boş olabilir. |

## Bağlantılar

- [ProductInfo.md](ProductInfo.md) — Liste elemanı
- [../../CustomerSupportBot.Application/Tools/ProductToolsService.md](../../CustomerSupportBot.Application/Tools/ProductToolsService.md) — Tek tüketici
- [../../CustomerSupportBot.Adapters.Persistence/PostgresAdapters.md](../../CustomerSupportBot.Adapters.Persistence/PostgresAdapters.md) — Üreten repo
