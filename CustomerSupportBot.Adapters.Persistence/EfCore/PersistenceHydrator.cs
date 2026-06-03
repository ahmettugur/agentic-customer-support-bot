// Adapters.Persistence/EfCore/PersistenceHydrator.cs
// Startup hydrator IHostedService.
// PostgreSQL provider aktifken uygulama açılışında çalışır:
//   1. ApprovalQueue: Pending kayıtlardan eski olanları (TCS kayboldu) Expired yap.
//   2. ReasoningTraceStore: CompletedAt=null olan in-flight trace'leri
//      Error="terminated_by_restart" olarak kapat.
//   3. Auth: Default admin kullanıcısını seed et (yoksa).
//   4. HumanAgents: Default temsilcileri seed et (tablo boşsa).
//   5. Workflows: Örnek workflow tanımlarını seed et (tablo boşsa).
// İşlemler idempotent. Hata olursa uygulama durmaz, sadece loglanır.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public sealed class PersistenceHydrator : IHostedService
{
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PersistenceHydrator> _logger;

    public PersistenceHydrator(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<PersistenceHydrator> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Hydrator] Startup recovery başlıyor.");

        // Approval queue — yetim Pending'leri Expired'a çek.
        try
        {
            if (GetService<IApprovalQueue>() is PostgresApprovalQueue pgApproval)
            {
                await pgApproval.ExpirePendingOnStartupAsync(StalePendingThreshold, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Approval expire başarısız.");
        }

        // Reasoning trace — yarım kalmış trace'leri kapat.
        try
        {
            if (GetService<IReasoningTraceStore>() is PostgresReasoningTraceStore pgTrace)
            {
                await pgTrace.MarkInflightAsErrorOnStartupAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Trace recovery başarısız.");
        }

        // Auth — default admin kullanıcısı yoksa oluştur.
        try { await SeedDefaultAdminAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Default admin seed başarısız.");
        }

        // HumanAgents — tablo boşsa default temsilcileri seed et.
        try { await SeedDefaultAgentsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Default agent seed başarısız.");
        }

        // Catalog — tablolar boşsa demo verilerini seed et.
        try { await SeedDefaultCategoriesAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Category seed başarısız.");
        }

        try { await SeedDefaultCustomersAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Customer seed başarısız.");
        }

        try { await SeedDefaultProductsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Product seed başarısız.");
        }

        try { await SeedDefaultOrdersAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Order seed başarısız.");
        }

        try { await SeedDefaultOrderDetailsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] OrderDetail seed başarısız.");
        }

        try { await SeedDefaultComplaintsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Complaint seed başarısız.");
        }

        try { await SeedDefaultWorkflowsAsync(cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Workflow seed başarısız.");
        }

        _logger.LogInformation("[Hydrator] Startup recovery tamam.");
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
            "[Hydrator] Default admin oluşturuldu (username='{Username}'). " +
            "ÜRETIMDE Auth:DefaultAdminPassword'u rotate edin.",
            username);
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
            "[Hydrator] 2 default human agent + kullanıcı hesabı seed edildi " +
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
        _logger.LogInformation("[Hydrator] {Count} kategori seed edildi.", NorthwindSeedData.Categories().Length);
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

        _logger.LogInformation("[Hydrator] {Count} müşteri seed edildi.", NorthwindSeedData.Customers().Length);
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

        _logger.LogInformation("[Hydrator] {Count} Northwind ürünü seed edildi.", NorthwindSeedData.Products().Length);
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

        _logger.LogInformation("[Hydrator] {Count} Northwind siparişi seed edildi.", NorthwindSeedData.Orders().Length);
    }

    private async Task SeedDefaultOrderDetailsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.OrderDetails.AnyAsync(ct)) return;

        ctx.OrderDetails.AddRange(NorthwindSeedData.OrderDetails());
        await ctx.SaveChangesAsync(ct);
        _logger.LogInformation("[Hydrator] {Count} sipariş detayı seed edildi.", NorthwindSeedData.OrderDetails().Length);
    }

    private async Task SeedDefaultComplaintsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        if (dbFactory is null) return;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        if (await ctx.Complaints.AnyAsync(ct)) return;

        ctx.Complaints.AddRange(NorthwindSeedData.Complaints());
        await ctx.SaveChangesAsync(ct);

        _logger.LogInformation("[Hydrator] {Count} demo şikayet seed edildi.", NorthwindSeedData.Complaints().Length);
    }

    private async Task SeedDefaultWorkflowsAsync(CancellationToken ct)
    {
        var dbFactory = GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var store    = GetService<IWorkflowDefinitionStore>();
        if (dbFactory is null || store is null) return;

        var now     = DateTime.UtcNow;
        var samples = BuildSampleWorkflows(now);

        // Sistem workflow'larını her zaman upsert et (emoji/encoding düzeltmeleri için).
        // Admin'in değiştirdiği workflow'lara dokunmaz çünkü Upsert UpdatedBy'ı korur;
        // sadece "system" tarafından son güncellenenleri yeniler.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var systemIds = samples.Select(s => s.Id).ToList();
        var existingBySystem = await ctx.WorkflowDefinitions
            .Where(w => systemIds.Contains(w.Id) && w.UpdatedBy == "system")
            .Select(w => w.Id)
            .ToListAsync(ct);

        int upserted = 0;
        foreach (var wf in samples)
        {
            // Yeni kayıt veya sistem tarafından son kaydedilmiş kayıt → güncelle
            if (!await ctx.WorkflowDefinitions.AnyAsync(w => w.Id == wf.Id, ct)
                || existingBySystem.Contains(wf.Id))
            {
                store.Upsert(wf, "system");
                upserted++;
            }
        }

        if (upserted > 0)
            _logger.LogInformation("[Hydrator] {Count} sistem workflow'u seed/güncellendi.", upserted);
    }

    private static WorkflowDefinition[] BuildSampleWorkflows(DateTime now)
    {
        return
        [
            // ── 1. Sipariş Durum Sorgulama ───────────────────────────────────────
            new WorkflowDefinition
            {
                Id          = "siparis-durumu",
                Name        = "Siparis Durum Sorgulama",
                Description = "4+ haneli siparis numarasi varsa OrderStatus tool'unu cagirır, yoksa numarayi ister.",
                IsActive    = true,
                Version     = 1,
                CreatedAt   = now,
                UpdatedAt   = now,
                UpdatedBy   = "system",
                StartStepId     = "br1",
                TriggerKeywords = ["siparis", "kargo", "durum", "nerede", "takip"],
                InputPatterns   = new Dictionary<string, string> { ["orderId"] = @"(\d{4,})" },
                Steps =
                [
                    new WorkflowStep
                    {
                        Id        = "br1",
                        Type      = WorkflowStepType.Branch,
                        Label     = "Siparis no var mi?",
                        Condition = "orderId exists",
                        OnTrue    = "lk1",
                        OnFalse   = "r_ask"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_ask",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Numara iste",
                        Template = "Siparisınizi sorgulayabilmem icin lutfen siparis numaranizi paylasin. (or: 1030)"
                    },
                    new WorkflowStep
                    {
                        Id         = "lk1",
                        Type       = WorkflowStepType.Lookup,
                        Label      = "Siparis sorgula",
                        Tool       = WellKnown.ToolNames.OrderStatus,
                        Parameters = new Dictionary<string, string> { ["orderId"] = "$orderId" },
                        StoreAs    = "siparis",
                        Next       = "r_result"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_result",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Sonucu goster",
                        Template = "Siparis #{orderId} durumu:\n\n{siparis}"
                    }
                ]
            },

            // ── 2. Ürün Kategorisi Listeleme ─────────────────────────────────────
            new WorkflowDefinition
            {
                Id          = "urun-kategori-listesi",
                Name        = "Urun Kategorisi Listeleme",
                Description = "Belirtilen kategorideki urunleri LLM cagirmadan product_list_tool ile listeler.",
                IsActive    = true,
                Version     = 1,
                CreatedAt   = now,
                UpdatedAt   = now,
                UpdatedBy   = "system",
                StartStepId     = "br1",
                TriggerKeywords = ["urunler", "liste", "neler var", "ne satiyorsunuz", "kategori"],
                InputPatterns   = new Dictionary<string, string>
                {
                    ["category"] = @"(laptop|notebook|telefon|smartphone|tablet|klavye|keyboard|mouse|fare|monitor|ekran|aksesuar|yazici|printer)"
                },
                Steps =
                [
                    new WorkflowStep
                    {
                        Id        = "br1",
                        Type      = WorkflowStepType.Branch,
                        Label     = "Kategori anlasildi mi?",
                        Condition = "category exists",
                        OnTrue    = "lk1",
                        OnFalse   = "r_ask"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_ask",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Kategori sor",
                        Template = "Hangi kategorideki urunlerimizi gormek istersiniz?\n\nMevcut kategoriler: Laptop, Telefon, Tablet, Klavye, Mouse, Monitor, Aksesuar, Yazici"
                    },
                    new WorkflowStep
                    {
                        Id         = "lk1",
                        Type       = WorkflowStepType.Lookup,
                        Label      = "Urun listesi al",
                        Tool       = WellKnown.ToolNames.ProductList,
                        Parameters = new Dictionary<string, string> { ["category"] = "$category" },
                        StoreAs    = "liste",
                        Next       = "r_result"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_result",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Listeyi goster",
                        Template = "{category} kategorisindeki urunlerimiz:\n\n{liste}"
                    }
                ]
            },

            // ── 3. Ürün Bilgisi Sorgulama ────────────────────────────────────────
            new WorkflowDefinition
            {
                Id          = "urun-bilgisi",
                Name        = "Urun Bilgisi Sorgulama",
                Description = "Kullanicinin sorgusunu ProductInquiry tool'una iletir; bulunamazsa anlamli hata verir.",
                IsActive    = true,
                Version     = 1,
                CreatedAt   = now,
                UpdatedAt   = now,
                UpdatedBy   = "system",
                StartStepId     = "sv1",
                TriggerKeywords = ["hakkinda", "ozellikleri", "fiyati", "kac para", "bilgi ver"],
                InputPatterns   = new Dictionary<string, string>(),
                Steps =
                [
                    new WorkflowStep
                    {
                        Id            = "sv1",
                        Type          = WorkflowStepType.SetVariable,
                        Label         = "Sorguyu kaydet",
                        VariableName  = "query",
                        VariableValue = "{input}",
                        Next          = "lk1"
                    },
                    new WorkflowStep
                    {
                        Id         = "lk1",
                        Type       = WorkflowStepType.Lookup,
                        Label      = "Urun ara",
                        Tool       = WellKnown.ToolNames.ProductInquiry,
                        Parameters = new Dictionary<string, string> { ["productName"] = "$query" },
                        StoreAs    = "urun",
                        Next       = "br1"
                    },
                    new WorkflowStep
                    {
                        Id        = "br1",
                        Type      = WorkflowStepType.Branch,
                        Label     = "Urun bulundu mu?",
                        Condition = "urun.success == true",
                        OnTrue    = "r_ok",
                        OnFalse   = "r_err"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_ok",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Urun bilgisini goster",
                        Template = "Urun Bilgisi:\n\n{urun}"
                    },
                    new WorkflowStep
                    {
                        Id       = "r_err",
                        Type     = WorkflowStepType.Respond,
                        Label    = "Bulunamadi yaniti",
                        Template = "Uzgunüm, \"{query}\" icin bir urun bulamadim. Urun adini daha spesifik belirtir misiniz?"
                    }
                ]
            }
        ];
    }

    private T? GetService<T>() =>
        (T?)_services.GetService(typeof(T));
}
