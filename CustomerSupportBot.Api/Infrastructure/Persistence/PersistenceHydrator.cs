// Infrastructure/Persistence/PersistenceHydrator.cs
// Startup hydrator IHostedService.
// PostgreSQL provider aktifken uygulama açılışında çalışır:
//   1. ApprovalQueue: Pending kayıtlardan eski olanları (TCS kayboldu) Expired yap.
//   2. ReasoningTraceStore: CompletedAt=null olan in-flight trace'leri
//      Error="terminated_by_restart" olarak kapat.
//   3. Auth: Default admin kullanıcısını seed et (yoksa).
//   4. HumanAgents: Default temsilcileri seed et (tablo boşsa).
// İşlemler idempotent. Hata olursa uygulama durmaz, sadece loglanır.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Hitl;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services.Auth;
using CustomerSupportBot.Api.Services.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.Infrastructure.Persistence;

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
            if (_services.GetService<IApprovalQueue>() is PostgresApprovalQueue pgApproval)
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
            if (_services.GetService<IReasoningTraceStore>() is PostgresReasoningTraceStore pgTrace)
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

        _logger.LogInformation("[Hydrator] Startup recovery tamam.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedDefaultAdminAsync(CancellationToken ct)
    {
        var dbFactory = _services.GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = _services.GetService<IPasswordHasher>();
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
        var dbFactory = _services.GetService<IDbContextFactory<CustomerSupportDbContext>>();
        var hasher = _services.GetService<IPasswordHasher>();
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
}
