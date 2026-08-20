// Demo veri seed'inin ORTAM KAPISI.
//
// Bu seeder, boş bir veritabanında bilinen varsayılan parolalarla admin, onay yetkili agent ve
// müşteri hesapları açıyor. Eskiden hiçbir ortam kontrolü yoktu: boş bir üretim veritabanıyla
// ilk açılışta bu hesaplar üretimde oluşuyordu. Kapının kendisi kadar önemli olan, kapalıyken
// DB'ye HİÇ dokunulmaması — "oluşturdu ama sonra sildi" gibi bir aralık kalmamalı.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class DemoDataSeederEnvironmentGateTests
{
    /// <summary>
    /// Seed'in çalışıp çalışmadığını, servis sağlayıcıya dokunulup dokunulmadığından okuruz:
    /// seeder tüm işini scope açarak yapar, dolayısıyla hiç scope istenmemesi "hiçbir şey
    /// yazılmadı"ın gözlemlenebilir kanıtıdır.
    /// </summary>
    private sealed class TrackingServiceProvider : IServiceProvider
    {
        public int Requests { get; private set; }

        public object? GetService(Type serviceType)
        {
            Requests++;
            // Kapı açıkken bile testin gerçek DB'ye gitmesini istemiyoruz: null dönmek
            // seeder'ın kendi try/catch'ine düşer, ama SAYAÇ artmış olur — ölçtüğümüz bu.
            return null;
        }
    }

    private static (DemoDataSeeder Seeder, TrackingServiceProvider Provider) Build(
        params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var provider = new TrackingServiceProvider();
        return (new DemoDataSeeder(provider, config, NullLogger<DemoDataSeeder>.Instance), provider);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Seed_IsSkipped_OutsideDevelopment(string environment)
    {
        var (seeder, provider) = Build(("DOTNET_ENVIRONMENT", environment));

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        provider.Requests.Should().Be(0,
            "üretim benzeri bir ortamda varsayılan parolalı hesaplar hiç oluşturulmamalı");
    }

    /// <summary>
    /// Ortam değişkeni hiç verilmemişse karar Production yönünde olmalı. Yanlış yön burada
    /// tehlikeli: eksik yapılandırma sessizce "geliştirme" sayılırsa koruma en çok ihtiyaç
    /// duyulan kurulumda kapanır.
    /// </summary>
    [Fact]
    public async Task Seed_IsSkipped_WhenEnvironmentIsUnset()
    {
        var (seeder, provider) = Build();

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        provider.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Seed_Runs_InDevelopment()
    {
        var (seeder, provider) = Build(("DOTNET_ENVIRONMENT", "Development"));

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        provider.Requests.Should().BeGreaterThan(0, "geliştirme akışı bozulmamalı");
    }

    /// <summary>Açık kaçış: üretim benzeri ortamda bilinçli olarak demo verisi isteyen kurulum.</summary>
    [Fact]
    public async Task Seed_Runs_WhenExplicitlyEnabled()
    {
        var (seeder, provider) = Build(
            ("DOTNET_ENVIRONMENT", "Production"), ("DemoData:Enabled", "true"));

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        provider.Requests.Should().BeGreaterThan(0);
    }

    /// <summary>Açık ayar, ortam kararını her iki yönde de ezmeli.</summary>
    [Fact]
    public async Task Seed_IsSkipped_WhenExplicitlyDisabled_EvenInDevelopment()
    {
        var (seeder, provider) = Build(
            ("DOTNET_ENVIRONMENT", "Development"), ("DemoData:Enabled", "false"));

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        provider.Requests.Should().Be(0);
    }
}
