// A2A hız sınırının BÖLÜMLEME ANAHTARI.
//
// Politika, anahtarı token claim'lerinden çıkarmak üzere yazılmıştı — partner başına limit.
// Ama rate limiter middleware'i UseAuthentication'dan ÖNCE çalışıyordu, dolayısıyla
// HttpContext.User o noktada boştu: claim hiç bulunamıyor ve politika sessizce IP'ye
// düşüyordu. Yani "partner başına" diye yazılan kural fiilen "IP başına" olarak işliyordu.
//
// İki yönlü zarar: aynı NAT ya da bulut çıkışı arkasındaki partnerler birbirinin kotasını
// tüketir; IP değiştirebilen bir istemci ise sınırı tamamen dolaşır.
//
// Ölçüm doğrudan bölümlemeye bakar: aynı IP'den gelen İKİ FARKLI partner. Anahtar kimlikse
// birinin limiti dolduğunda diğeri hâlâ geçebilmelidir.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class A2ARateLimitPartitionTests
{
    private const int PermitLimit = 3;

    private sealed class A2AFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("A2A:Enabled", "true");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["A2A:Enabled"] = "true",
                    ["A2A:RequestsPerMinute"] = PermitLimit.ToString(),
                    ["A2A:PublicBaseUrl"] = "https://example.test",
                }));
        }
    }

    private static string MintPartnerToken(A2AFactory factory, string partnerId)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: partnerId, Username: partnerId, PasswordHash: "", Role: A2ARoles.Partner,
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: null);
        return provider.GenerateAccessToken(user, DateTime.UtcNow).Token;
    }

    private static HttpClient ClientFor(A2AFactory factory, string partnerId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintPartnerToken(factory, partnerId));
        client.DefaultRequestHeaders.TryAddWithoutValidation("A2A-Version", "1.0");
        return client;
    }

    private static async Task<HttpStatusCode> CallAsync(HttpClient client, CancellationToken ct) =>
        (await client.PostAsJsonAsync("/a2a/product", new { }, ct)).StatusCode;

    /// <summary>
    /// ASIL BULGU. İki partner aynı IP'den (test sunucusunda hepsi aynıdır) çağırıyor.
    /// Birincinin kotası dolduktan sonra ikinci partner hâlâ geçebilmeli — anahtar kimlik
    /// olduğu için. Anahtar IP olsaydı ikinci partner de 429 alırdı.
    /// </summary>
    [Fact]
    public async Task OnePartnerExhaustingItsQuota_DoesNotBlockAnother()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new A2AFactory();

        var first = ClientFor(factory, $"partner-a-{Guid.NewGuid():N}");
        var second = ClientFor(factory, $"partner-b-{Guid.NewGuid():N}");

        // Birinci partnerin kotasını tüket.
        var sawThrottle = false;
        for (var i = 0; i < PermitLimit + 2; i++)
            if (await CallAsync(first, ct) == HttpStatusCode.TooManyRequests) sawThrottle = true;

        sawThrottle.Should().BeTrue("sınır gerçekten uygulanmalı — yoksa test hiçbir şey ölçmez");

        (await CallAsync(second, ct)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "sınır partner başına olmalı; ikinci partner birincinin kotasından etkilenmemeli");
    }

    /// <summary>
    /// Sınırın kendisi çalışmaya devam etmeli: aynı partner kotasını aştığında 429 almalı.
    /// Bu olmadan yukarıdaki test, sınırın tamamen kapalı olmasıyla da geçerdi.
    /// </summary>
    [Fact]
    public async Task TheSamePartnerIsThrottledAfterItsQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new A2AFactory();
        var client = ClientFor(factory, $"partner-c-{Guid.NewGuid():N}");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < PermitLimit + 2; i++)
            statuses.Add(await CallAsync(client, ct));

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
