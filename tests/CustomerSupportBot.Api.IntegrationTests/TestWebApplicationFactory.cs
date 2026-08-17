// Tests/Endpoints/TestWebApplicationFactory.cs
// WebApplicationFactory: InMemory persistence + dummy AI config kullanır.
// Redis bağımlılığı test ortamında InMemoryDistributedLock ile override edilir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Locking;

namespace CustomerSupportBot.Api.IntegrationTests;

public class TestWebApplicationFactory : WebApplicationFactory<global::Program>
{
    // Aynı factory boyunca aynı db-name kullanılsın ki seed edilen kullanıcılar
    // HTTP request'lerde de görünsün.
    private readonly string _dbName = $"test-db-{Guid.NewGuid():N}";

    public IDbContextFactory<CustomerSupportDbContext> GetDbContextFactory()
        => Services.GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // NOT: "Persistence:Provider" ayarı bilinçli olarak set EDİLMEZ.
        // PersistenceProvider enum'ında artık yalnızca Postgres var (InMemory adapter'ları
        // DI'dan kaldırıldı), "InMemory" değeri IOptions binding'ini startup'ta patlatıyordu.
        // Test izolasyonu aşağıda DbContextFactory'nin UseInMemoryDatabase ile
        // override edilmesiyle sağlanıyor — provider ayarı zaten hiçbir yerde okunmuyor.
        builder.UseSetting("AI:Provider", "OpenAI");
        builder.UseSetting("AI:ApiKey", "test-key");
        builder.UseSetting("AI:ModelId", "gpt-4o-mini");
        builder.UseSetting(
            "Jwt:SigningKey",
            "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!");
        // Redis connection string — AddRedisServices zorunlu kılıyor ama
        // test ortamında gerçek Redis yok; aşağıda IAppDistributedLock override ediliyor.
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false");

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
            });
        });

        // Auth servisleri DbContextFactory ister; testte EF InMemory provider kullanırız.
        builder.ConfigureServices(services =>
        {
            // AddCustomerSupportPersistence artık koşulsuz UseNpgsql kaydediyor
            // (eski "Persistence:Provider=InMemory" şalteri kaldırıldı). EF tek service
            // provider'da iki database provider'a izin vermediği için CustomerSupportDbContext'e
            // ait TÜM EF kayıtlarını (options, options-configuration, factory, pooling)
            // temizleyip yerine InMemory provider'ı koyuyoruz.
            RemoveEfRegistrationsFor<CustomerSupportDbContext>(services);

            services.AddDbContextFactory<CustomerSupportDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName));

            // InMemory modda PersistenceServicesExtensions auth repo'larını kaydetmez;
            // test ortamı için EF Core implementasyonlarını manuel olarak ekle.
            services.AddScoped<IUserAuthRepository, EfUserAuthRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

            // Redis bağlantısını ve distributed lock'u test-only InMemory ile değiştir.
            // Bu sayede integration testleri gerçek Redis sunucusuna ihtiyaç duymaz.
            services.RemoveAll<IConnectionMultiplexer>();
            services.Replace(ServiceDescriptor.Singleton<IAppDistributedLock>(
                new InMemoryDistributedLock(
                    Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }))));

            // IConnectionMultiplexer kaldırıldığı için Redis tabanlı message bus da
            // çözümlenemez hâle geliyor (PostgresApprovalQueue/EscalationSink/ChatBridge
            // hepsi IMessageBusPort ister). Süreç-içi InMemory ikizine geçiyoruz.
            services.Replace(ServiceDescriptor
                .Singleton<IMessageBusPort, InMemoryMessageBusAdapter>());
        });

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Belirtilen DbContext tipine ait tüm EF Core kayıtlarını service collection'dan siler.
    /// <c>RemoveAll&lt;IDbContextFactory&lt;T&gt;&gt;()</c> tek başına yetmez: provider seçimi
    /// <c>IDbContextOptionsConfiguration&lt;T&gt;</c> kayıtlarında saklanır ve bunlar birikir.
    /// </summary>
    private static void RemoveEfRegistrationsFor<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        var contextType = typeof(TContext);

        var doomed = services.Where(d =>
                d.ServiceType == contextType
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericArguments().Contains(contextType)))
            .ToList();

        foreach (var d in doomed)
            services.Remove(d);
    }
}

