using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Ürün kataloğu için secondary port.
///</summary>
public interface IProductCatalogRepository
{
    /// <summary>Tam eşleşme veya fuzzy match ile ürün bulur. Bulunamazsa null döner.</summary>
    ProductInfo? FindProduct(string productName);

    /// <summary>Stok azaltır. Yetersiz stokta false döner.</summary>
    bool TryDeductStock(string productName, int quantity);

    /// <summary>Tüm ürün listesi.</summary>
    IReadOnlyList<ProductInfo> GetAll();

    /// <summary>Belirli kategorideki ürünleri döner.</summary>
    IReadOnlyList<ProductInfo> GetByCategory(string category);

    /// <summary>Katalogdaki tüm benzersiz kategori adlarını sıralı döner.</summary>
    IReadOnlyList<string> GetCategories();
}
