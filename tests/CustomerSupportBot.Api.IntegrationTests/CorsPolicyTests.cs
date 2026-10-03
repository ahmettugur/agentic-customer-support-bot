// Boş origin listesinin ANLAMI.
//
// Cors:AllowedOrigins boş bırakıldığında politika AllowAnyOrigin'e düşüyordu — ve depodaki
// varsayılan appsettings.json'da liste boş. Yani üretim override'ı unutulduğunda her site
// API'yi tarayıcıdan çağırabiliyordu. Kimlik bearer token ile taşındığı için bu doğrudan bir
// token sızıntısı değil, ama "yapılandırılmadıysa herkese açık" bir güvenlik sınırı için
// yanlış varsayılandır. Development'ta (Blazor :5288 ↔ API :5021) kolaylık korunur.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CustomerSupportBot.Api.IntegrationTests;

public class CorsPolicyTests
{
    private sealed class EnvFactory : TestWebApplicationFactory
    {
        public required string Environment { get; init; }
        public string? AllowedOrigin { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(Environment);
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                var values = new Dictionary<string, string?>
                {
                    // Üretimde ayrıca zorunlu olan ayar; yanlış nedenle patlamasın.
                    ["A2A:PublicBaseUrl"] = "https://example.test",
                };
                if (AllowedOrigin is not null) values["Cors:AllowedOrigins:0"] = AllowedOrigin;
                cfg.AddInMemoryCollection(values);
            });
        }
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/chat/");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await client.SendAsync(request, ct);
    }

    [Fact]
    public async Task OutsideDevelopment_EmptyOriginList_AllowsNoCrossOriginCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new EnvFactory { Environment = "Production" };

        var response = await PreflightAsync(factory.CreateClient(), "https://kotu-site.example", ct);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "yapılandırılmamış origin listesi üretimde 'herkese açık' anlamına gelmemeli");
    }

    [Fact]
    public async Task OutsideDevelopment_ConfiguredOrigin_IsAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new EnvFactory { Environment = "Production", AllowedOrigin = "https://panel.example" };

        var response = await PreflightAsync(factory.CreateClient(), "https://panel.example", ct);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be("https://panel.example");
    }

    [Fact]
    public async Task InDevelopment_EmptyOriginList_StaysPermissive()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new EnvFactory { Environment = "Development" };

        var response = await PreflightAsync(factory.CreateClient(), "http://localhost:5288", ct);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue(
            "yerel geliştirmede Blazor istemcisi (:5288) ayrı origin'den çağırır");
    }
}
