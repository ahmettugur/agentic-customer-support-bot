using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Ürün kataloğu için secondary port.
///</summary>
public interface IProductCatalogRepository
{
    /// <summary>Tam eşleşme veya fuzzy match ile ürün bulur. Bulunamazsa null döner.</summary>
    ProductInfo? FindProduct(string nameOrPartial);

    /// <summary>Stok azaltır. Yetersiz stokta false döner.</summary>
    bool TryDeductStock(string productName, int quantity);

    /// <summary>Tüm ürün listesi.</summary>
    IReadOnlyList<ProductInfo> GetAll();
}
