using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace CustomerSupportBot.Tests.Shared;

/// <summary>
/// Paylaşımlı Testcontainers Postgres fixture — CatalogRepository entegrasyon testleri için.
/// Tam Northwind seed verisi ile başlatılır.
/// </summary>
public sealed class PostgresCatalogFixture : IAsyncLifetime
{
    // Alpine imajı ICU collation'larını (ör. "und-u-ks-level1", ProductConfiguration/CategoryConfiguration'da
    // kullanılıyor) içermiyor — Debian tabanlı "postgres:16" kullanılmalı.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public IDbContextFactory<CustomerSupportDbContext> DbFactory { get; private set; } = null!;

    public OrderRepository OrderRepo { get; private set; } = null!;
    public ProductCatalogRepository ProductRepo { get; private set; } = null!;
    public ComplaintRepository ComplaintRepo { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // EnableRetryOnFailure ÜRETİMLE AYNI olmalı (bkz. PersistenceServiceCollectionExtensions).
        // Bu satır olmadan strateji, elle açılan transaction'lara izin veren varsayılan
        // ExecutionStrategy olur; üretimde ise NpgsqlRetryingExecutionStrategy'dir ve
        // BeginTransaction'ı InvalidOperationException ile reddeder. Fark, çok satırlı
        // sipariş/stok transaction'larının testlerde geçip canlıda patlamasına yol açmıştı.
        var options = new DbContextOptionsBuilder<CustomerSupportDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history", "public");
                npg.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .Options;

        DbFactory = new SimpleDbContextFactory(options);

        await using var ctx = DbFactory.CreateDbContext();

        // MigrateAsync — EnsureCreatedAsync DEĞİL. Şemayı modelden üreten EnsureCreated,
        // InitialCreate migration'ındaki `CREATE COLLATION "und-u-ks-level1"` raw SQL'ini
        // çalıştırmıyor; Product.Name/Category.Name kolonları o collation'ı istediği için
        // CREATE TABLE "collation does not exist" hatasıyla düşüyordu. Migration yolu
        // hem collation'ı oluşturur hem de migration'ların kendisini doğrular.
        await ctx.Database.MigrateAsync();

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

        // catalog.complaint_seq BİLEREK burada yaratılmıyor: ComplaintRepository.Create
        // ona nextval() ile bağımlı ve sequence'i InitialCreate migration'ı oluşturuyor.
        // Fixture kendi kopyasını yaratırsa migration'dan düşmesi testlerde görünmez olur —
        // sequence'in migration'da unutulduğu asıl hata (42P01) tam olarak böyle kaçmıştı.
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('catalog.products','id'), (SELECT MAX(id) FROM catalog.products)); " +
            // Seed satırları AÇIK kod değerleriyle (1030, 1042, …) eklendiği için identity
            // sequence'leri ilerlemiyor ve yeni kayıtlar 1'den başlıyordu — seed'in üstüne
            // çıkacak şekilde senkronize et.
            "SELECT setval(pg_get_serial_sequence('catalog.orders','code'), (SELECT MAX(code) FROM catalog.orders)); " +
            "SELECT setval(pg_get_serial_sequence('catalog.complaints','code'), (SELECT MAX(code) FROM catalog.complaints));");
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
