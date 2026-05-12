// Tests/Endpoints/TestWebApplicationFactory.cs
// WebApplicationFactory: InMemory persistence + dummy AI config kullanır.
// Redis bağımlılığı test ortamında InMemoryDistributedLock ile override edilir.
using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services.Locking;
using CustomerSupportBot.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CustomerSupportBot.Tests.Endpoints;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    // Aynı factory boyunca aynı db-name kullanılsın ki seed edilen kullanıcılar
    // HTTP request'lerde de görünsün.
    private readonly string _dbName = $"test-db-{Guid.NewGuid():N}";

    public IDbContextFactory<CustomerSupportDbContext> GetDbContextFactory()
        => Services.GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:Provider", "InMemory");
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
                ["Persistence:Provider"] = "InMemory",
                ["Jwt:SigningKey"] = "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
            });
        });

        // Auth servisleri DbContextFactory ister; testte EF InMemory provider kullanırız.
        builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CustomerSupportDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName));

            // Redis bağlantısını ve distributed lock'u test-only InMemory ile değiştir.
            // Bu sayede integration testleri gerçek Redis sunucusuna ihtiyaç duymaz.
            services.RemoveAll<IConnectionMultiplexer>();
            services.Replace(ServiceDescriptor.Singleton<IAppDistributedLock>(
                new InMemoryDistributedLock(
                    Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }))));
        });

        builder.UseEnvironment("Development");
    }
}

