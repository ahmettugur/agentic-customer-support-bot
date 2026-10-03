// URL'deki token (?access_token=) HANGİ uçlarda geçerli?
//
// Tarayıcının EventSource ve WebSocket API'leri Authorization header'ı taşıyamaz; bu yüzden
// SSE/WS uçları token'ı query string'den okur. Ama bu kabul TÜM uçlara açıktı: normal JSON
// uçları da URL'deki token'ı kabul ediyordu. URL'deki token erişim loglarına, proxy
// kayıtlarına ve tarayıcı geçmişine düşer; kısa ömürlü olsa da taşıması gereken yer yalnızca
// header'ı taşıyamayan istemcilerin kullandığı uçlardır.

using System.Net;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class QueryStringTokenScopeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public QueryStringTokenScopeTests(TestWebApplicationFactory factory) => _factory = factory;

    private string CustomerToken()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: "user-1001", Username: "1001@example.com", PasswordHash: "", Role: "Customer",
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: "1001");
        return provider.GenerateAccessToken(user, DateTime.UtcNow).Token;
    }

    [Fact]
    public async Task ARegularJsonEndpoint_IgnoresATokenInTheQueryString()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/customer/approvals/history?access_token={Uri.EscapeDataString(CustomerToken())}", ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "header taşıyabilen uçlar URL'deki token'ı kabul etmemeli");
    }

    [Fact]
    public async Task TheSseEventStream_StillAcceptsATokenInTheQueryString()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            $"/chat/events/{Guid.NewGuid()}?access_token={Uri.EscapeDataString(CustomerToken())}",
            HttpCompletionOption.ResponseHeadersRead, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "EventSource header taşıyamaz — bu uçta query token gerekli");
    }
}
