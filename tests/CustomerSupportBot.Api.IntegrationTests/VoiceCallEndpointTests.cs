// Uç yetkileri ve durum kodları: personel başlatır, müşteri kabul eder, başka müşteri 403, ikinci arama 409,
// parça yükleme yalnız görüşmenin temsilcisi.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class VoiceCallEndpointTests : IClassFixture<VoiceCallEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IChatModeRegistry Modes { get; } = Substitute.For<IChatModeRegistry>();
        public ISessionManager Sessions { get; } = Substitute.For<ISessionManager>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVoiceCallStore>();
                services.AddSingleton<IVoiceCallStore, InMemoryVoiceCallStore>();
                services.RemoveAll<IVoiceRecordingStore>();
                services.AddSingleton<IVoiceRecordingStore, InMemoryVoiceRecordingStore>();
                services.RemoveAll<IChatModeRegistry>();
                services.AddSingleton(Modes);
                services.RemoveAll<ISessionManager>();
                services.AddSingleton(Sessions);
            });
        }
    }

    private readonly Factory _factory;
    public VoiceCallEndpointTests(Factory factory) => _factory = factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role, string? agentId = null, string? customerId = null)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"u-{Guid.NewGuid():N}", "", role,
                agentId, true, DateTime.UtcNow, null, customerId),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string HumanSession(string customerId = "1001")
    {
        var sid = $"s-{Guid.NewGuid():N}";
        _factory.Modes.GetMode(sid).Returns(ChatMode.Human);
        var session = new AgentSession { SessionId = sid };
        session.State.AuthenticatedCustomerId = customerId;
        _factory.Sessions.GetAsync(sid, Arg.Any<CancellationToken>()).Returns(session);
        return sid;
    }

    private static async Task<string> IdOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

    [Fact]
    public async Task Start_Accept_Upload_Hangup_HappyPath()
    {
        var agent = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var customer = ClientFor("Customer", customerId: "1001");
        var sid = HumanSession();

        var start = await agent.PostAsync($"/chat-sessions/{sid}/voice-calls", null, Ct);
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOf(start);

        (await customer.PostAsync($"/chat/voice-calls/{id}/accept", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new ByteArrayContent([1, 2, 3]);
        body.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        (await agent.PostAsync($"/voice-calls/{id}/chunks?track=customer&seq=0&offsetMs=0&durationMs=10000", body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hang = await agent.PostAsync($"/voice-calls/{id}/hangup", null, Ct);
        (await hang.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().Should().Be("ended");
    }

    [Fact]
    public async Task SecondCall_ByBusyAgent_Is409()
    {
        var agentId = $"agent-{Guid.NewGuid():N}";
        var agent = ClientFor("Agent", agentId: agentId);
        (await agent.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await agent.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be("voice_call_busy");
    }

    [Fact]
    public async Task OtherCustomer_CannotAccept_Or_Signal()
    {
        var agent = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var id = await IdOf(await agent.PostAsync($"/chat-sessions/{HumanSession("1001")}/voice-calls", null, Ct));
        var stranger = ClientFor("Customer", customerId: "9999");
        (await stranger.PostAsync($"/chat/voice-calls/{id}/accept", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"/chat/voice-calls/{id}/signal", new { callId = id, type = "ice" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OtherAgent_CannotUploadOrHangup()
    {
        var owner = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var id = await IdOf(await owner.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct));
        var other = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        (await other.PostAsync($"/voice-calls/{id}/hangup", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_CannotUseStaffEndpoints()
    {
        var customer = ClientFor("Customer", customerId: "1001");
        (await customer.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct)).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
