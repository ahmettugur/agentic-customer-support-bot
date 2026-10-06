// "general" hız sınırının BÖLÜMLEME ANAHTARI ve kotası.
//
// Eskiden tüm "general" trafiği IP başına 60/dk idi. Admin/temsilci paneli 15 sn'de bir yoklama yapar
// (rozetler + aktif sekme), açık canlı sohbette 5 sn'de bir duygu durumu, SLA sayfası 5 sn'de bir iki
// istek atar. Aynı IP'deki (yerelde hepsi 127.0.0.1; kurumda aynı NAT) yönetici, temsilci ve müşteri
// ekranları tek 60'lık kotayı paylaşınca paneller sürekli 429 alıyordu. Artık kimliği doğrulanmış
// personel KULLANICI başına ve daha yüksek bir kotayla, müşteri MÜŞTERİ başına, kimliksiz istek IP
// başına bölümlenir.

using System.Net;
using System.Net.Http.Headers;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class GeneralRateLimitPartitionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient Client(WebApplicationFactory<global::Program> factory, string role, string userId, string? customerId = null)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(userId, $"{userId}-user", "", role, role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, customerId),
            DateTime.UtcNow).Token;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<List<HttpStatusCode>> HitAsync(HttpClient client, string url, int times)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < times; i++)
            statuses.Add((await client.GetAsync(url, Ct)).StatusCode);
        return statuses;
    }

    private static WebApplicationFactory<global::Program> Factory(params (string Key, string Value)[] settings)
    {
        var root = new TestWebApplicationFactory();
        return root.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });
    }

    [Fact]
    public async Task AdminPollingThePanel_IsNotCutOffAtTheOldPerIpLimit()
    {
        using var factory = Factory();

        var statuses = await HitAsync(Client(factory, "Admin", "admin-1"), "/approvals/pending", 70);

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests, "eski IP başına 60/dk sınırı paneli kilitliyordu");
    }

    [Fact]
    public async Task StaffUsersOnTheSameIp_HaveSeparateQuotas()
    {
        using var factory = Factory(("RateLimiting:StaffPerMinute", "5"));
        var admin = Client(factory, "Admin", "admin-1");

        (await HitAsync(admin, "/approvals/pending", 6)).Should().Contain(HttpStatusCode.TooManyRequests, "personel kotası uygulanır");

        (await HitAsync(Client(factory, "Admin", "admin-2"), "/approvals/pending", 1))
            .Should().NotContain(HttpStatusCode.TooManyRequests, "başka bir yönetici ilk yöneticinin kotasından etkilenmemeli");
        (await HitAsync(Client(factory, "Agent", "agent-user-1"), "/agent/approvals/pending", 1))
            .Should().NotContain(HttpStatusCode.TooManyRequests, "aynı IP'deki temsilci de ayrı kotada");
    }

    [Fact]
    public async Task Customers_ArePartitionedByCustomer_WithTheGeneralLimit()
    {
        using var factory = Factory(("RateLimiting:GeneralPerMinute", "3"));

        (await HitAsync(Client(factory, "Customer", "c-user-1", "1001"), "/customer/approvals/history", 4))
            .Should().Contain(HttpStatusCode.TooManyRequests);
        (await HitAsync(Client(factory, "Customer", "c-user-2", "1002"), "/customer/approvals/history", 1))
            .Should().NotContain(HttpStatusCode.TooManyRequests, "aynı IP'deki başka bir müşteri etkilenmemeli");
    }
}
