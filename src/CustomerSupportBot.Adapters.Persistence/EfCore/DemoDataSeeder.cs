// Adapters.Persistence/EfCore/DemoDataSeeder.cs
// Startup IHostedService — boş bir veritabanında hızlı başlamak için demo/deneme verisi
// seed eder (default admin, agent'lar, Northwind ürün/müşteri/sipariş verisi, demo müşteri
// login hesabı). PersistenceHydrator'dan (restart-sonrası veri bütünlüğü temizliği) BİLİNÇLİ
// olarak ayrı tutuldu — bu sınıf üretimde tamamen kapatılabilir/kaldırılabilir, diğeri kapatılamaz.
// İşlemler idempotent (her seed metodu kendi tablosu boşsa/kayıt yoksa çalışır).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public sealed class DemoDataSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<DemoDataSeeder> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Seeder] Demo veri seed başlıyor.");

        // Auth — default admin kullanıcısı yoksa oluştur.
        try { await SeedDefaultAdminAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Default admin seed başarısız.");
        }

        // A2A partner hesabı — YALNIZCA kanal açıkken. Kapalı kurulumda kimlik oluşturmak,
        // kullanılmayan bir erişim yüzeyi bırakmak olurdu.
        try { await SeedA2APartnerAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] A2A partner seed başarısız.");
        }

        // HumanAgents — tablo boşsa default temsilcileri seed et.
        try { await SeedDefaultAgentsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Default agent seed başarısız.");
        }

        // Catalog — tablolar boşsa demo verilerini seed et.
        try { await SeedDefaultCategoriesAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Category seed başarısız.");
        }

        try { await SeedDefaultCustomersAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Customer seed başarısız.");
        }

        // Demo müşteri login hesabı — catalog.customers seed'inden SONRA çalışmalı
        // (linked_customer_id geçerli bir CustomerEntity'ye işaret etmeli).
        try { await SeedDefaultCustomerAccountAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Default customer account seed başarısız.");
        }

        try { await SeedDefaultProductsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Product seed başarısız.");
        }

        try { await SeedDefaultOrdersAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Order seed başarısız.");
        }

        try { await SeedDefaultOrderDetailsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] OrderDetail seed başarısız.");
        }

        try { await SeedDefaultComplaintsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeder] Complaint seed başarısız.");
        }

        _logger.LogInformation("[Seeder] Demo veri seed tamam.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedDefaultAdminAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = GetService<IPasswordHasher>();
        if (dbFactory is null || hasher is null) return;

        var username = _configuration["Auth:DefaultAdminUsername"] ?? "admin";
        var password = _configuration["Auth:DefaultAdminPassword"] ?? "Admin123!";

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var exists = await ctx.Users.AnyAsync(u => u.Username == username, ct);
        if (exists) return;

        ctx.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[Seeder] Default admin oluşturuldu (username='{Username}'). " +
            "ÜRETIMDE Auth:DefaultAdminPassword'u rotate edin.",
            username);
    }

    /// <summary>
    /// A2A partner login hesabı — <b>yalnızca geliştirme/demo</b> içindir.
    ///
    /// <para>
    /// Token değişimi (<c>/auth/a2a/token-exchange</c>) <c>Partner</c> rolünde bir kimlik ister,
    /// ama seed'de öyle bir hesap yoktu ve oluşturmanın da bir yolu yoktu — yani A2A zinciri
    /// uçtan uca hiç denenemiyordu. Bu metot o boşluğu geliştirme ortamı için kapatır.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Üretimde gerçek partner sağlama ayrı bir iştir</b> (admin onaylı kayıt akışı,
    /// gizli rotasyonu, sözleşme kapsamı). Buradaki hesap sabit bir varsayılan paroladan türer
    /// ve <b>üretimde kullanılmamalıdır</b>.
    /// </para>
    ///
    /// <para>
    /// Yalnızca <c>A2A:Enabled=true</c> iken çalışır: kapalı kurulumda partner kimliği
    /// oluşturmak, hiç kullanılmayacak bir erişim yüzeyi bırakmak olurdu. Ayrıca hesabın var
    /// olması TEK BAŞINA yetmez — partnerin hangi müşteriler adına hareket edebileceği
    /// <c>A2A:Partners</c> altında ayrıca tanımlanmalıdır (varsayılan: hiçbiri).
    /// </para>
    /// </summary>
    private async Task SeedA2APartnerAsync(CancellationToken ct)
    {
        if (!_configuration.GetValue<bool>("A2A:Enabled")) return;

        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = GetService<IPasswordHasher>();
        if (dbFactory is null || hasher is null) return;

        var username = _configuration["A2A:DevPartnerUsername"] ?? "demo-partner";
        var password = _configuration["A2A:DevPartnerPassword"] ?? "Partner123!";

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Users.AnyAsync(u => u.Username == username, ct)) return;

        ctx.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = "Partner",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[Seeder] A2A DEV partner hesabı oluşturuldu (username='{Username}'). " +
            "Bu hesap yalnızca geliştirme içindir; üretimde kullanmayın ve parolayı rotate edin.",
            username);
    }

    /// <summary>
    /// Demo/deneme amaçlı bir müşteri login hesabı — Northwind seed'indeki CustomerId=1027
    /// (Ahmet Tügür) kaydına bağlı. Böylece Chat sayfasını login akışıyla denemek için
    /// önce manuel kayıt olmaya gerek kalmaz.
    /// </summary>
    private async Task SeedDefaultCustomerAccountAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = GetService<IPasswordHasher>();
        if (dbFactory is null || hasher is null) return;

        var email = _configuration["Auth:DefaultCustomerEmail"] ?? "ahmet.tugur@example.com";
        var password = _configuration["Auth:DefaultCustomerPassword"] ?? "Customer123!";
        var customerId = _configuration["Auth:DefaultCustomerLinkedId"] ?? "1027";

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var exists = await ctx.Users.AnyAsync(u => u.Username == email, ct);
        if (exists) return;

        ctx.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = email,
            PasswordHash = hasher.Hash(password),
            Role = "Customer",
            LinkedCustomerId = customerId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[Seeder] Demo müşteri hesabı oluşturuldu (email='{Email}', customerId={CustomerId}). " +
            "ÜRETİMDE Auth:DefaultCustomerPassword'u rotate edin veya bu seed'i kapatın.",
            email, customerId);
    }

    private async Task SeedDefaultAgentsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = GetService<IPasswordHasher>();
        if (dbFactory is null || hasher is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var hasAny = await ctx.HumanAgents.AnyAsync(ct);
        if (hasAny) return;

        var now = DateTime.UtcNow;
        var defaultPassword = _configuration["Auth:DefaultAgentPassword"] ?? "Agent123!";

        var agents = new[]
        {
            new HumanAgentEntity
            {
                Id = "agent-jdoe",
                DisplayName = "John Doe",
                Email = "john.doe@example.com",
                SkillsJson = "[\"complaint\",\"refund\",\"vip\"]",
                LanguagesJson = "[\"en\",\"tr\"]",
                IsActive = true,
                MaxConcurrentLoad = 5,
                CurrentLoad = 0,
                Priority = 1,
                CreatedAt = now
            },
            new HumanAgentEntity
            {
                Id = "agent-jsmith",
                DisplayName = "Jane Smith",
                Email = "jane.smith@example.com",
                SkillsJson = "[\"order\",\"product\",\"enterprise\"]",
                LanguagesJson = "[\"en\"]",
                IsActive = true,
                MaxConcurrentLoad = 8,
                CurrentLoad = 0,
                Priority = 0,
                CreatedAt = now
            }
        };

        ctx.HumanAgents.AddRange(agents);

        // Her agent için "Agent" rolünde login hesabı oluştur
        foreach (var agent in agents)
        {
            var username = agent.Email!.Split('@')[0]; // john.doe, jane.smith
            var exists = await ctx.Users.AnyAsync(u => u.Username == username, ct);
            if (!exists)
            {
                ctx.Users.Add(new UserEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username,
                    PasswordHash = hasher.Hash(defaultPassword),
                    Role = "Agent",
                    LinkedAgentId = agent.Id,
                    IsActive = true,
                    CreatedAt = now
                });
            }
        }

        await ctx.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[Seeder] 2 default human agent + kullanıcı hesabı seed edildi " +
            "(john.doe / jane.smith). ÜRETİMDE Auth:DefaultAgentPassword'u rotate edin.");
    }

    private async Task SeedDefaultCategoriesAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Categories.AnyAsync(ct)) return;

        ctx.Categories.AddRange(NorthwindSeedData.Categories());
        await ctx.SaveChangesAsync(ct);
        _logger.LogInformation("[Seeder] {Count} kategori seed edildi.", NorthwindSeedData.Categories().Length);
    }

    private async Task SeedDefaultCustomersAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Customers.AnyAsync(ct)) return;

        ctx.Customers.AddRange(NorthwindSeedData.Customers());
        await ctx.SaveChangesAsync(ct);

        // Identity sequence'ını seed verilerinin üstüne çek
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('catalog.customers', 'id'), COALESCE((SELECT MAX(id) FROM catalog.customers), 1029))",
            ct);

        _logger.LogInformation("[Seeder] {Count} müşteri seed edildi.", NorthwindSeedData.Customers().Length);
    }

    private async Task SeedDefaultProductsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Products.AnyAsync(ct)) return;

        ctx.Products.AddRange(NorthwindSeedData.Products());
        await ctx.SaveChangesAsync(ct);

        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('catalog.products','id'), (SELECT MAX(id) FROM catalog.products))",
            ct);

        _logger.LogInformation("[Seeder] {Count} Northwind ürünü seed edildi.", NorthwindSeedData.Products().Length);
    }

    private async Task SeedDefaultOrdersAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Orders.AnyAsync(ct)) return;

        ctx.Orders.AddRange(NorthwindSeedData.Orders());
        await ctx.SaveChangesAsync(ct);

        // Identity sequence'ını seed verilerinin üstüne çek
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('catalog.orders', 'code'), COALESCE((SELECT MAX(code) FROM catalog.orders), 1081))",
            ct);

        _logger.LogInformation("[Seeder] {Count} Northwind siparişi seed edildi.", NorthwindSeedData.Orders().Length);
    }

    private async Task SeedDefaultOrderDetailsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.OrderDetails.AnyAsync(ct)) return;

        ctx.OrderDetails.AddRange(NorthwindSeedData.OrderDetails());
        await ctx.SaveChangesAsync(ct);
        _logger.LogInformation("[Seeder] {Count} sipariş detayı seed edildi.", NorthwindSeedData.OrderDetails().Length);
    }

    private async Task SeedDefaultComplaintsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Complaints.AnyAsync(ct)) return;

        ctx.Complaints.AddRange(NorthwindSeedData.Complaints());
        await ctx.SaveChangesAsync(ct);

        _logger.LogInformation("[Seeder] {Count} demo şikayet seed edildi.", NorthwindSeedData.Complaints().Length);
    }

    private T? GetService<T>() =>
        (T?)_services.GetService(typeof(T));
}
