// Yüklü prompt sürümlerini gösteren yönetici ucu: yalnızca Admin, prompt METNİ dönmez (yalnızca anahtar + sürüm).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class PromptEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public PromptEndpointTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: Guid.NewGuid().ToString("N"), Username: $"u-{role}", PasswordHash: "", Role: role,
            LinkedAgentId: role == "Agent" ? "agent-1" : null, IsActive: true,
            CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: role == "Customer" ? "1001" : null);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    [Fact]
    public async Task Admin_GetsSetVersionAndPerPromptVersions_WithoutPromptText()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Client("Admin").GetAsync("/prompts", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("setVersion").GetString().Should().MatchRegex("^[0-9a-f]{8}$");
        var prompts = json.GetProperty("prompts").EnumerateArray().ToList();
        prompts.Should().NotBeEmpty();
        prompts.Should().Contain(p => p.GetProperty("key").GetString() == "agents/order-agent");
        prompts.Should().OnlyContain(p => p.GetProperty("version").GetString()!.Length == 8);
        (await response.Content.ReadAsStringAsync(ct)).Should().NotContain("content", "prompt metni dönmemeli");
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Customer")]
    public async Task NonAdmins_AreRejected(string role)
    {
        var response = await Client(role).GetAsync("/prompts", TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var response = await _factory.CreateClient().GetAsync("/prompts", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
