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

    public bool TryDeductStock(string productName, int quantity)
    {
        using var ctx = _dbFactory.CreateDbContext();

        var affected = ctx.Products
            .Where(p => p.Name == productName && p.Stock >= quantity)
            .ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity));

        return affected > 0;
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
