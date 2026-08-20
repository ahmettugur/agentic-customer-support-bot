// İmzalama anahtarı BAŞLATMA KAPISI.
//
// JwtAccessTokenProvider'ın tek kontrolü "boş değil ve >= 32 karakter" — depoya işlenmiş yer
// tutucu bunu SAĞLIYOR. Yani üretim override'ı unutulduğunda uygulama gayet sağlıklı açılıyor,
// ama imzalama anahtarı herkesçe bilinir oluyor: o anahtarla Admin/Agent/Customer token'ı
// üretmek mümkün, dolayısıyla sistemdeki tüm yetkilendirme kontrolleri anlamsızlaşıyor.
// Sessizce devam edilebilecek bir durum olmadığı için kapı uyarı değil, başlatma hatasıdır.

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CustomerSupportBot.Api.IntegrationTests;

public class JwtSigningKeyGuardTests
{
    private sealed class GuardFactory : TestWebApplicationFactory
    {
        public required string Environment { get; init; }
        public required string SigningKey { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(Environment);
            // Taban factory anahtarı iki yerden veriyor (UseSetting + AddInMemoryCollection);
            // sonuncusu kazandığı için testin değeri de aynı katmandan verilmeli.
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = SigningKey,
                    // Üretim ortamında ayrıca zorunlu olan ayar; onun kapısına takılıp
                    // yanlış nedenle patlamayalım diye burada karşılanır.
                    ["A2A:PublicBaseUrl"] = "https://example.test",
                }));
        }
    }

    /// <summary>
    /// Anahtarı literal olarak tekrar yazmak yerine appsettings.json'daki GERÇEK değeri
    /// okuruz: dosyadaki yer tutucu bir gün değişirse test onunla birlikte değişmeli,
    /// eski bir kopyayı doğrulayıp sessizce anlamsızlaşmamalı.
    /// </summary>
    private static string CommittedSigningKey()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Jwt").GetProperty("SigningKey").GetString()!;
    }

    [Fact]
    public void CommittedAppSettings_StillCarriesAPlaceholderKey()
    {
        // Bu testin varlık sebebi: aşağıdaki testin gerçekten bir yer tutucuyu
        // reddettiğinden emin olmak.
        CommittedSigningKey().Should().Contain("REPLACE_IN_PRODUCTION");
    }

    [Fact]
    public void Startup_Fails_WhenCommittedPlaceholderKeyIsUsedOutsideDevelopment()
    {
        using var factory = new GuardFactory
        {
            Environment = "Production",
            SigningKey = CommittedSigningKey(),
        };

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SigningKey*");
    }

    [Theory]
    [InlineData("CHANGE_ME_this_is_not_a_real_signing_key_at_all!!")]
    [InlineData("placeholder_signing_key_that_is_long_enough_x!!!!")]
    public void Startup_Fails_ForOtherPlaceholderMarkers(string key)
    {
        using var factory = new GuardFactory { Environment = "Production", SigningKey = key };

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Karşı yön: gerçek bir anahtarla üretim başlatması engellenmemeli — kapı çalışan
    /// kurulumu kilitlememelidir.
    /// </summary>
    [Fact]
    public void Startup_Succeeds_WithARealKeyOutsideDevelopment()
    {
        using var factory = new GuardFactory
        {
            Environment = "Production",
            SigningKey = "b7f2c1a95e4d38af6021cc7be9d5148a3f60d2e7c9ab415e",
        };

        var act = () => factory.CreateClient();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Development'ta yer tutucu kullanmak NORMAL — geliştirici kurulumu kapıya takılmamalı.
    /// </summary>
    [Fact]
    public void Startup_Succeeds_WithPlaceholderInDevelopment()
    {
        using var factory = new GuardFactory
        {
            Environment = "Development",
            SigningKey = CommittedSigningKey(),
        };

        var act = () => factory.CreateClient();

        act.Should().NotThrow();
    }
}
