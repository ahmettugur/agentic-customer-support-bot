// Ters proxy arkasında İSTEMCİ IP'si.
//
// IP tabanlı hız sınırları (auth, general) Connection.RemoteIpAddress'e bakar. Load balancer
// arkasında bu adres her istek için PROXY'nin adresidir: tüm kullanıcılar tek bir kotayı
// paylaşır, tek bir saldırgan herkesi login'den kilitler. Gerçek istemci adresi proxy'nin
// eklediği X-Forwarded-For başlığındadır.
//
// Ama bu başlık istemci tarafından da yazılabilir. Yalnızca YAPILANDIRILMIŞ güvenilir
// proxy'lerden gelen başlık dikkate alınmalı; aksi hâlde her istekte farklı bir değer
// gönderen istemci IP sınırını tamamen dolaşır.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ForwardedHeadersRateLimitTests
{
    private const string Proxy = "10.0.0.5";
    private const int AuthLimit = 3;

    /// <summary>
    /// TestServer'da TCP bağlantısı yoktur; karşı ucun adresini (proxy mi, doğrudan istemci
    /// mi) uygulamanın boru hattından ÖNCE çalışan bu filtre bir test başlığından ayarlar.
    /// </summary>
    private sealed class PeerAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-Peer", out var peer))
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }

    private sealed class ProxiedFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:AuthRateLimitPerMinute", AuthLimit.ToString());
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", Proxy);
            builder.ConfigureServices(s => s.AddTransient<IStartupFilter, PeerAddressStartupFilter>());
        }
    }

    private static async Task<HttpStatusCode> LoginAsync(
        HttpClient client, string peer, string forwardedFor, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { Username = "saldirgan", Password = "yanlis" })
        };
        request.Headers.Add("X-Test-Peer", peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await client.SendAsync(request, ct)).StatusCode;
    }

    [Fact]
    public async Task BehindAKnownProxy_EachClientGetsItsOwnQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ProxiedFactory();
        var client = factory.CreateClient();

        var first = new List<HttpStatusCode>();
        for (var i = 0; i < AuthLimit + 1; i++)
            first.Add(await LoginAsync(client, Proxy, "203.0.113.1", ct));

        first.Should().Contain(HttpStatusCode.TooManyRequests);
        (await LoginAsync(client, Proxy, "203.0.113.2", ct)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "aynı proxy arkasındaki başka bir istemci, ilkinin kotasından etkilenmemeli");
    }

    [Fact]
    public async Task FromAnUntrustedPeer_ForwardedForIsIgnored_SoSpoofingDoesNotBypassTheLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ProxiedFactory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < AuthLimit + 2; i++)
            statuses.Add(await LoginAsync(client, "198.51.100.7", $"203.0.113.{i + 10}", ct));

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "güvenilir proxy olmayan bir karşı ucun X-Forwarded-For başlığı IP sınırını değiştirmemeli");
    }
}
