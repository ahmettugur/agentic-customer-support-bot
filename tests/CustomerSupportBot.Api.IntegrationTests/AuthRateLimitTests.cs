// /auth/* uçlarının HIZ SINIRI.
//
// Bu uçlar (login, customer/login, customer/register, refresh) doğası gereği kimliksizdir —
// AllowAnonymous. Hiçbir sınır yoktu: kimlik bilgisi tahmin etme (credential stuffing / brute
// force) ve kayıt spam'i tek bir istemciden ucu bucaksız denenebiliyordu.
//
// Ölçüm doğrudan sınırın uygulandığını doğrular — yanlış şifreyle çağrılıp 401 alınsa bile
// istek sayaca girmelidir, aksi hâlde "hiç doğru şifre bilmeyen" bir saldırgan sınırdan
// tamamen muaf kalırdı.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace CustomerSupportBot.Api.IntegrationTests;

public class AuthRateLimitTests
{
    /// <summary>
    /// Taban factory bu limiti 100000'e çıkarır ki paylaşılan test altyapısı (aynı loopback
    /// IP'yi paylaşan onlarca meşru login çağrısı) kendi sınırına takılmasın. Bu sınıf,
    /// sınırın GERÇEKTEN uygulandığını ölçmek için değeri kasıtlı olarak geri düşürür.
    /// </summary>
    private sealed class LowLimitFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:AuthRateLimitPerMinute", "10");
        }
    }

    [Fact]
    public async Task Login_IsThrottled_AfterRepeatedAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new LowLimitFactory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var resp = await client.PostAsJsonAsync(
                "/auth/login", new { Username = "saldirgan", Password = $"tahmin-{i}" }, ct);
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.Unauthorized,
            "yanlış şifreli denemeler normalde 401 almalı — sınırın kendisini değil, doğru davranışı ölçüyoruz");
        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "kimlik bilgisi tahmin etme tek istemciden sınırsız denenebilmemeli");
    }

    /// <summary>Kayıt ucu da aynı grubun altında — spam'e karşı korunmalı.</summary>
    [Fact]
    public async Task CustomerRegister_IsThrottled_AfterRepeatedAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new LowLimitFactory();
        var client = factory.CreateClient();

        var sawThrottle = false;
        for (var i = 0; i < 15; i++)
        {
            var resp = await client.PostAsJsonAsync("/auth/customer/register", new
            {
                Email = $"spam{i}@example.com", Password = "GucluParola1", CustomerId = "9999"
            }, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) sawThrottle = true;
        }

        sawThrottle.Should().BeTrue();
    }
}
