// Ports/Driven/Persistence/IProductCatalogRepository.cs
// SECONDARY PORT — Ürün kataloğu erişimi.
// Mevcut: FakeDatabase statik bağımlılığı bu port'a dönüştürülür.
// Adaptörler: InMemoryProductCatalog (FakeDatabase'i sarar), ileride PostgresProductCatalog

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Ürün kataloğu için secondary port.
/// CustomerSupportTools (AI tool'ları) FakeDatabase yerine bu porta bağımlı olacak.
/// </summary>
public interface IProductCatalogRepository
{
    /// <summary>Tam eşleşme veya fuzzy match ile ürün bulur. Bulunamazsa null döner.</summary>
    ProductInfo? FindProduct(string nameOrPartial);

    /// <summary>Stok azaltır. Yetersiz stokta false döner.</summary>
    bool TryDeductStock(string productName, int quantity);

    /// <summary>Tüm ürün listesi.</summary>
    IReadOnlyList<ProductInfo> GetAll();
}
