// Temsilci asistanı uçları — yetki ve yönlendirme. Asıl iş mantığı AgentAssistServiceTests'te;
// burada port sahte bir uygulamayla değiştirilir (LLM'e gidilmez).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class AgentAssistEndpointTests : IClassFixture<AgentAssistEndpointTests.Factory>
{
    public const string KnownSession = "assist-known";

    private sealed class FakeAssist : IAgentAssistPort
    {
        public Task<AgentAssistResult?> GetAssistAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(sessionId == KnownSession
                ? new AgentAssistResult(sessionId, "Özet", "Talep", "Taslak", null,
                    new AgentAssistSentiment("neutral", 0.5, 0), null, [], [])
                : null);
    }

    public sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IAgentAssistPort, FakeAssist>()));
        }
    }

    private readonly Factory _factory;
    public AgentAssistEndpointTests(Factory factory) => _factory = factory;

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

    [Theory]
    [InlineData("Admin", "/chat-sessions/")]
    [InlineData("Agent", "/agent/chat-sessions/")]
    public async Task StaffGetTheAssist(string role, string prefix)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Client(role).GetAsync($"{prefix}{KnownSession}/assist", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AgentAssistResult>(ct);
        body!.SuggestedReply.Should().Be("Taslak");
    }

    [Fact]
    public async Task UnknownSession_Is404()
    {
        var response = await Client("Agent").GetAsync(
            "/agent/chat-sessions/yok/assist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/chat-sessions/")]
    [InlineData("/agent/chat-sessions/")]
    public async Task Customers_CannotSeeTheAssist(string prefix)
    {
        var response = await Client("Customer").GetAsync(
            $"{prefix}{KnownSession}/assist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var response = await _factory.CreateClient().GetAsync(
            $"/agent/chat-sessions/{KnownSession}/assist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
