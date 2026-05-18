// Adapters.Persistence/InMemory/InMemoryProductCatalogAdapter.cs
// DRIVEN ADAPTER — IProductCatalogRepository → InMemory (FakeDatabase sarar).
//
// Hexagonal dönüşümün en kritik adımı: FakeDatabase statik bağımlılığı bu adapter'a
// hapsedilir. Core artık FakeDatabase'i bilmez; sadece IProductCatalogRepository port'una
// bağımlıdır. İleride PostgresProductCatalogAdapter yazıldığında sıfır Core değişikliği.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

/// <summary>
/// FakeDatabase ürün kataloğunu IProductCatalogRepository port'una bağlar.
/// </summary>
public sealed class InMemoryProductCatalogAdapter : IProductCatalogRepository
{
    // FakeDatabase seed verisi buraya taşındı — artık statik global değil, DI ile inject edilir.
    private readonly ConcurrentDictionary<string, ProductInfo> _catalog = new()
    {
        ["Dell XPS 15"]                   = new ProductInfo(1500, 10),
        ["Apple iPhone 15 Pro"]           = new ProductInfo(1200, 15),
        ["Sony WH-1000XM5 Headphones"]    = new ProductInfo(350,  20),
        ["Samsung Galaxy Tab S9"]         = new ProductInfo(900,  12),
        ["Logitech MX Master 3S Mouse"]   = new ProductInfo(100,  30)
    };

    private readonly object _stockLock = new();

    public ProductInfo? FindProduct(string nameOrPartial)
    {
        foreach (var (key, value) in _catalog)
        {
            if (key.Contains(nameOrPartial, StringComparison.OrdinalIgnoreCase))
                return value with { Name = key };
        }
        return null;
    }

    public bool TryDeductStock(string productName, int quantity)
    {
        if (!_catalog.TryGetValue(productName, out var product)) return false;

        lock (_stockLock)
        {
            if (product.Stock < quantity) return false;
            _catalog[productName] = product with { Stock = product.Stock - quantity };
            return true;
        }
    }

    public IReadOnlyList<ProductInfo> GetAll() =>
        _catalog.Select(kv => kv.Value with { Name = kv.Key }).ToList();
}
