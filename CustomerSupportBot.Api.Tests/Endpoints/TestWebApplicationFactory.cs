// Tests/Endpoints/TestWebApplicationFactory.cs
// WebApplicationFactory: InMemory persistence + dummy AI config kullanýr.
// Redis baðýmlýlýðý test ortamýnda InMemoryDistributedLock ile override edilir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services.Locking;
using CustomerSupportBot.Api.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CustomerSupportBot.Api.Tests.Endpoints;

public class TestWebApplicationFactory : WebApplicationFactory<global::Program>
{
    // Ayný factory boyunca ayný db-name kullanýlsýn ki seed edilen kullanýcýlar
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
        // Redis connection string — AddRedisServices zorunlu kýlýyor ama
        // test ortamýnda gerçek Redis yok; aþaðýda IAppDistributedLock override ediliyor.
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

        // Auth servisleri DbContextFactory ister; testte EF InMemory provider kullanýrýz.
        builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CustomerSupportDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName));

            // Redis baðlantýsýný ve distributed lock'u test-only InMemory ile deðiþtir.
            // Bu sayede integration testleri gerçek Redis sunucusuna ihtiyaç duymaz.
            services.RemoveAll<IConnectionMultiplexer>();
            services.Replace(ServiceDescriptor.Singleton<IAppDistributedLock>(
                new InMemoryDistributedLock(
                    Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }))));
        });

        builder.UseEnvironment("Development");
    }
}

