using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class ProductCatalogRepository : IProductCatalogRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<ProductCatalogRepository> _logger;

    public ProductCatalogRepository(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<ProductCatalogRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public ProductInfo? FindProduct(string productName)
    {
        using var ctx = _dbFactory.CreateDbContext();
        
        var product = ctx.Products.AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefault(p => p.Name == productName);

        return product is null ? null : new ProductInfo(product.Price, product.Stock, product.Name, product.Category.Name);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Atomiklik tek bir transaction ile sağlanır: her satır koşullu bir UPDATE
    /// (<c>Stock &gt;= quantity</c>) ile düşülür; herhangi biri 0 satır etkilerse
    /// transaction commit edilmez ve o ana kadar düşülenler geri alınır.
    ///
    /// <para>
    /// Satırlar ürün adına göre sıralı işlenir. Bu kozmetik değil: iki eşzamanlı sipariş
    /// aynı iki ürünü ters sırada kilitlerse Postgres deadlock verir. Sabit bir sıra
    /// kilitleme düzenini deterministik yapar.
    /// </para>
    /// </remarks>
    public StockDeductionResult TryDeductStock(IReadOnlyList<OrderLine> lines)
    {
        if (lines.Count == 0) return StockDeductionResult.Ok();

        using var ctx = _dbFactory.CreateDbContext();
        using var tx = ctx.Database.BeginTransaction();

        var shortages = new List<StockShortage>();

        foreach (var line in lines.OrderBy(l => l.Product, StringComparer.Ordinal))
        {
            var qty = line.Quantity;
            var affected = ctx.Products
                .Where(p => p.Name == line.Product && p.Stock >= qty)
                .ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock - qty));

            if (affected > 0) continue;

            // Düşüm başarısız — sebebini kullanıcıya söyleyebilmek için mevcut stoğu oku.
            // Ürün hiç yoksa Available=0 raporlanır; ürünün varlığı zaten çağrıdan önce
            // FindProduct ile doğrulanmış olmalı, bu yalnızca yarış durumu için savunmadır.
            var available = ctx.Products
                .Where(p => p.Name == line.Product)
                .Select(p => (int?)p.Stock)
                .FirstOrDefault() ?? 0;

            shortages.Add(new StockShortage(line.Product, qty, available));
        }

        if (shortages.Count > 0)
        {
            tx.Rollback();
            _logger.LogInformation(
                "Stok düşümü tamamı geri alındı — yetersiz satır sayısı: {Count}", shortages.Count);
            return StockDeductionResult.Insufficient(shortages);
        }

        tx.Commit();
        return StockDeductionResult.Ok();
    }

    public IReadOnlyList<ProductInfo> GetAll()
    {
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .Select(p => new ProductInfo(p.Price, p.Stock, p.Name, p.Category.Name))
            .ToList();
    }

    public IReadOnlyList<ProductInfo> GetByCategory(string category)
    {
        using var ctx = _dbFactory.CreateDbContext();

        // Name kolonu collation ile tanımlı — == operatörü case+accent insensitive eşleşir.
        var matched = ctx.Categories.AsNoTracking()
            .FirstOrDefault(c => c.Name == category);

        if (matched is null) return [];

        return ctx.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.CategoryId == matched.Id)
            .OrderBy(p => p.Name)
            .Select(p => new ProductInfo(p.Price, p.Stock, p.Name, p.Category.Name))
            .ToList();
    }

    public IReadOnlyList<string> GetCategories()
    {
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToList();
    }
    
}
