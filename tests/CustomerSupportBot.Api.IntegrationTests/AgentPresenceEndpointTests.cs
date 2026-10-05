// Temsilci durumu uçları: temsilci kendi durumunu yönetir, yönetici herkesinkini görür.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class AgentPresenceEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role, string? linkedAgentId = null)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..20], "", role,
                linkedAgentId, true, DateTime.UtcNow, null, null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string NewAgent()
    {
        var id = $"pr{Guid.NewGuid():N}"[..12];
        factory.Services.GetRequiredService<IHumanAgentPort>()
            .CreateAgent(new HumanAgent { Id = id, DisplayName = "Durum " + id, IsActive = true });
        return id;
    }

    private static async Task<string?> PresenceOf(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("presence").GetString();
    }

    [Fact]
    public async Task Agent_ConnectsChoosesAndHeartbeats_AdminSeesIt()
    {
        var id = NewAgent();
        var agent = ClientFor("Agent", id);

        (await PresenceOf(await agent.PostAsync("/agent/presence/connect", null, Ct))).Should().Be("online");
        (await PresenceOf(await agent.PutAsJsonAsync("/agent/presence", new { presence = "away" }, Ct))).Should().Be("away");
        (await PresenceOf(await agent.PostAsync("/agent/presence/heartbeat", null, Ct))).Should().Be("away");
        (await PresenceOf(await agent.GetAsync("/agent/presence", Ct))).Should().Be("away");

        var all = await ClientFor("Admin").GetFromJsonAsync<JsonElement>("/agents/presence", Ct);
        var mine = all.EnumerateArray().Single(a => a.GetProperty("agentId").GetString() == id);
        mine.GetProperty("presence").GetString().Should().Be("away");
        mine.GetProperty("displayName").GetString().Should().Be("Durum " + id);
    }

    [Fact]
    public async Task InvalidPresence_Is400()
    {
        var agent = ClientFor("Agent", NewAgent());

        (await agent.PutAsJsonAsync("/agent/presence", new { presence = "busy" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await agent.PutAsJsonAsync("/agent/presence", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoLinkedAgent_Is400_UnknownAgent_Is404()
    {
        (await ClientFor("Agent").PostAsync("/agent/presence/connect", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClientFor("Agent", "kayitsiz-temsilci").PostAsync("/agent/presence/connect", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyAdmin_SeesEveryonesPresence()
    {
        (await ClientFor("Agent", NewAgent()).GetAsync("/agents/presence", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/agent/presence", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
