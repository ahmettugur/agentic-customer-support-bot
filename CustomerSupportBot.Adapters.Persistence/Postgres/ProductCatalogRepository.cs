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

    public ProductInfo? FindProduct(string nameOrPartial)
    {
        using var ctx = _dbFactory.CreateDbContext();

        var normalized = nameOrPartial.ToLower();

        // Önce tam eşleşme (case-insensitive), sonra substring — tek context, iki query
        var exact = ctx.Products.AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefault(p => p.Name.ToLower() == normalized);
        if (exact is not null)
            return new ProductInfo(exact.Price, exact.Stock, exact.Name, exact.Category.Name);

        var partial = ctx.Products.AsNoTracking()
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .FirstOrDefault(p => p.Name.ToLower().Contains(normalized));

        return partial is null ? null : new ProductInfo(partial.Price, partial.Stock, partial.Name, partial.Category.Name);
    }

    public bool TryDeductStock(string productName, int quantity)
    {
        using var ctx = _dbFactory.CreateDbContext();

        // Atomik: WHERE stock >= quantity kontrolü ile tek UPDATE
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
        return ctx.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.Category.Name.ToLower() == category.ToLower())
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
