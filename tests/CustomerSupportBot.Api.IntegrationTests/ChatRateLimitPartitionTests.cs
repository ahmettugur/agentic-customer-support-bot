// Sohbet hız sınırının BÖLÜMLEME ANAHTARI.
//
// "chat" politikası her turda birden çok LLM çağrısı tetikleyen uçları korur — asıl korunan
// şey maliyettir. Anahtar IP olduğunda iki yönlü zarar vardı: aynı NAT/kurumsal çıkış
// arkasındaki müşteriler tek bir 20/dk kotasını paylaşıyor (biri diğerlerini kilitliyor),
// IP değiştirebilen tek bir hesap ise sınırı tamamen dolaşıyordu. Uçlar zaten Customer
// token'ı istediği için doğru anahtar müşteri kimliğidir.
//
// Ölçüm: aynı IP'den (test sunucusunda hepsi aynıdır) gelen İKİ FARKLI müşteri. Birinin
// kotası dolduğunda diğeri hâlâ geçebilmeli.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ChatRateLimitPartitionTests
{
    /// <summary>Program'daki "chat" politikasının dakikalık izni.</summary>
    private const int ChatPermitLimit = 20;

    private static HttpClient CustomerClient(TestWebApplicationFactory factory, string customerId)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: $"user-{customerId}", Username: $"{customerId}@example.com", PasswordHash: "",
            Role: "Customer", LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow,
            LastLoginAt: null, LinkedCustomerId: customerId);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    /// <summary>
    /// Boş sorgu InputGuard'da 400 ile reddedilir — LLM'e hiç gitmez, ama hız sınırı
    /// endpoint'ten ÖNCE çalıştığı için istek sayaca girer.
    /// </summary>
    private static async Task<HttpStatusCode> ChatAsync(HttpClient client, CancellationToken ct) =>
        (await client.PostAsJsonAsync("/chat/", new { query = "" }, ct)).StatusCode;

    [Fact]
    public async Task OneCustomerExhaustingItsQuota_DoesNotBlockAnotherOnTheSameIp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new TestWebApplicationFactory();
        var first = CustomerClient(factory, "1001");
        var second = CustomerClient(factory, "1002");

        var firstStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < ChatPermitLimit + 1; i++)
            firstStatuses.Add(await ChatAsync(first, ct));

        firstStatuses.Should().Contain(HttpStatusCode.TooManyRequests, "sınır müşteri başına uygulanmalı");
        (await ChatAsync(second, ct)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "aynı IP'yi paylaşan başka bir müşteri, ilk müşterinin kotasından etkilenmemeli");
    }
}
