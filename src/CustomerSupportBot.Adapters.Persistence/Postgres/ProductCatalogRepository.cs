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

    /// <summary>
    /// Bir işi kendi transaction'ı içinde, <b>retry stratejisiyle uyumlu</b> biçimde çalıştırır.
    ///
    /// <para>
    /// Üretimde <c>EnableRetryOnFailure</c> açık (bkz. <c>PersistenceServiceCollectionExtensions</c>),
    /// yani strateji <c>NpgsqlRetryingExecutionStrategy</c>'dir ve bu strateji elle açılmış
    /// (user-initiated) transaction'ları REDDEDER: geçici bir hatada yalnızca tek bir komutu
    /// yeniden denemek, çok komutlu bir transaction'ı yarıda bırakabileceği için güvenli
    /// değildir. Çözüm, transaction'ın <b>tamamını</b> stratejiye tek bir yeniden-denenebilir
    /// birim olarak vermektir.
    /// </para>
    ///
    /// <para>
    /// <see cref="DbContext"/> delegate'in İÇİNDE yaratılır: yeniden denemede taze bir
    /// change-tracker gerekir, aksi hâlde ilk denemede eklenmiş entity'ler ikinci denemede
    /// tekrar yazılırdı.
    /// </para>
    ///
    /// <para>
    /// Delegate <c>Commit</c> alanını <c>false</c> döndürürse transaction commit edilmez ve
    /// <c>using</c> dispose'unda geri alınır — "iş mantığı gereği vazgeç" ile "hata oldu"
    /// ayrımı böylece exception fırlatmadan ifade edilir.
    /// </para>
    /// </summary>
    private T ExecuteInTransaction<T>(Func<CustomerSupportDbContext, (bool Commit, T Result)> work)
    {
        // Strateji context'ten okunur ama iş için kullanılmaz; asıl context delegate içinde açılır.
        using var probe = _dbFactory.CreateDbContext();
        var strategy = probe.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using var ctx = _dbFactory.CreateDbContext();
            using var tx = ctx.Database.BeginTransaction();

            var (commit, result) = work(ctx);
            if (commit) tx.Commit();
            return result;
        });
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

    public CategoryProducts GetByCategory(string category)
    {
        using var ctx = _dbFactory.CreateDbContext();

        // Name kolonu collation ile tanımlı — == operatörü case+accent insensitive eşleşir.
        var matched = ctx.Categories.AsNoTracking()
            .FirstOrDefault(c => c.Name == category);

        // "Kategori yok" ile "kategori boş" burada ayrılır; ikisini de boş listeye
        // indirmek çağıranın hata mesajını doğru kurmasını imkânsız kılıyordu.
        if (matched is null) return CategoryProducts.NotFound();

        var products = ctx.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.CategoryId == matched.Id)
            .OrderBy(p => p.Name)
            .Select(p => new ProductInfo(p.Price, p.Stock, p.Name, p.Category.Name))
            .ToList();

        return CategoryProducts.Found(matched.Name, products);
    }

    public IReadOnlyList<string> GetSelectableCategories()
    {
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Categories
            .AsNoTracking()
            .Where(c => ctx.Products.Any(p => p.CategoryId == c.Id))
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToList();
    }
    
}
