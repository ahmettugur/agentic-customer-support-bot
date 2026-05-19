// Tests/Endpoints/TestWebApplicationFactory.cs
// WebApplicationFactory: InMemory persistence + dummy AI config kullan�r.
// Redis ba��ml�l��� test ortam�nda InMemoryDistributedLock ile override edilir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Domain.Model;
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
    // Ayn� factory boyunca ayn� db-name kullan�ls�n ki seed edilen kullan�c�lar
    // HTTP request'lerde de g�r�ns�n.
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
        // Redis connection string � AddRedisServices zorunlu k�l�yor ama
        // test ortam�nda ger�ek Redis yok; a�a��da IAppDistributedLock override ediliyor.
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

        // Auth servisleri DbContextFactory ister; testte EF InMemory provider kullan�r�z.
        builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CustomerSupportDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName));

            // Redis ba�lant�s�n� ve distributed lock'u test-only InMemory ile de�i�tir.
            // Bu sayede integration testleri ger�ek Redis sunucusuna ihtiya� duymaz.
            services.RemoveAll<IConnectionMultiplexer>();
            services.Replace(ServiceDescriptor.Singleton<IAppDistributedLock>(
                new InMemoryDistributedLock(
                    Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }))));
        });

        builder.UseEnvironment("Development");
    }
}

