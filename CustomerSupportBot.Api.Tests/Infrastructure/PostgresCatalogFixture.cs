using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace CustomerSupportBot.Api.Tests.Infrastructure;

/// <summary>
/// Paylaşımlı Testcontainers Postgres fixture — CatalogRepository entegrasyon testleri için.
/// Tam Northwind seed verisi ile başlatılır.
/// </summary>
public sealed class PostgresCatalogFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public IDbContextFactory<CustomerSupportDbContext> DbFactory { get; private set; } = null!;

    public OrderRepository OrderRepo { get; private set; } = null!;
    public ProductCatalogRepository ProductRepo { get; private set; } = null!;
    public ComplaintRepository ComplaintRepo { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<CustomerSupportDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npg =>
                npg.MigrationsHistoryTable("__ef_migrations_history", "public"))
            .Options;

        DbFactory = new SimpleDbContextFactory(options);

        await using var ctx = DbFactory.CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();

        await SeedAsync();

        OrderRepo     = new OrderRepository(DbFactory,   NullLogger<OrderRepository>.Instance);
        ProductRepo   = new ProductCatalogRepository(DbFactory, NullLogger<ProductCatalogRepository>.Instance);
        ComplaintRepo = new ComplaintRepository(DbFactory, NullLogger<ComplaintRepository>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        await using var ctx = DbFactory.CreateDbContext();

        ctx.Categories.AddRange(NorthwindSeedData.Categories());
        ctx.Products.AddRange(NorthwindSeedData.Products());
        ctx.Orders.AddRange(NorthwindSeedData.Orders());
        ctx.OrderDetails.AddRange(NorthwindSeedData.OrderDetails());
        ctx.Complaints.AddRange(NorthwindSeedData.Complaints());

        await ctx.SaveChangesAsync();

        await ctx.Database.ExecuteSqlRawAsync(
            "CREATE SEQUENCE IF NOT EXISTS catalog.order_seq START WITH 1082; " +
            "CREATE SEQUENCE IF NOT EXISTS catalog.complaint_seq START WITH 1006; " +
            "SELECT setval(pg_get_serial_sequence('catalog.products','id'), (SELECT MAX(id) FROM catalog.products));");
    }

    private sealed class SimpleDbContextFactory : IDbContextFactory<CustomerSupportDbContext>
    {
        private readonly DbContextOptions<CustomerSupportDbContext> _options;
        public SimpleDbContextFactory(DbContextOptions<CustomerSupportDbContext> options) => _options = options;
        public CustomerSupportDbContext CreateDbContext() => new(_options);
        public Task<CustomerSupportDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }
}

[CollectionDefinition("PostgresCatalog")]
public sealed class PostgresCatalogCollection : ICollectionFixture<PostgresCatalogFixture>;
