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
    ///
    /// <para>
    /// Transaction <see cref="ExecutionStrategyExtensions"/> üzerinden çalıştırılır — bkz.
    /// <see cref="ExecuteInTransaction{T}"/>.
    /// </para>
    /// </remarks>
    public StockDeductionResult TryDeductStock(IReadOnlyList<OrderLine> lines)
    {
        if (lines.Count == 0) return StockDeductionResult.Ok();

        return ExecuteInTransaction(ctx =>
        {
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
                _logger.LogInformation(
                    "Stok düşümü tamamı geri alındı — yetersiz satır sayısı: {Count}", shortages.Count);
                // Commit edilmez → using tx dispose edilirken rollback olur.
                return (Commit: false, Result: StockDeductionResult.Insufficient(shortages));
            }

            return (Commit: true, Result: StockDeductionResult.Ok());
        });
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
