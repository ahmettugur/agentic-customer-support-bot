// Konuşma kapanışı uçları: durum kodları, kimin kapattığı, temsilci kimliği. Mantık
// ConversationClosingServiceTests'te.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ConversationClosingEndpointTests : IClassFixture<ConversationClosingEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IConversationClosingPort Closing { get; } = Substitute.For<IConversationClosingPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConversationClosingPort>();
                services.AddSingleton(Closing);
            });
        }
    }

    private readonly Factory _factory;

    public ConversationClosingEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Closing.ClearReceivedCalls();
        _factory.Closing.GetOptionsAsync(Arg.Any<CancellationToken>()).Returns(new ConversationClosingOptionsView(
            true, [new ClosingReasonOption("resolved", "Çözüldü")], ["kargo"]));
        _factory.Closing.CloseAsync(Arg.Any<string>(), Arg.Any<ConversationClosingInput>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(0) switch
            {
                "gecersiz" => new ConversationClosingResult(ConversationClosingStatus.Invalid, Error: "Kapanış nedeni seçin."),
                "bot-modda" => new ConversationClosingResult(ConversationClosingStatus.NotLive, Error: "Sohbet canlı değil."),
                var sid => new ConversationClosingResult(ConversationClosingStatus.Ok,
                    new ConversationDisposition { SessionId = sid, ReasonCode = "resolved", Tags = ["kargo"] }, EscalationsResolved: 1)
            });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role, string username = "kullanici")
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), username, "", role,
                role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Body => new { reason = "resolved", tags = new[] { "kargo" }, note = "tamam" };

    [Fact]
    public async Task AdminClose_PassesTheInputAndTheClosingUser()
    {
        var response = await ClientFor("Admin", "yonetici").PostAsJsonAsync("/chat-sessions/s-1/close", Body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("escalationsResolved").GetInt32().Should().Be(1);
        json.GetProperty("disposition").GetProperty("reasonCode").GetString().Should().Be("resolved");
        await _factory.Closing.Received(1).CloseAsync("s-1",
            Arg.Is<ConversationClosingInput>(i => i.Reason == "resolved" && i.Tags!.Single() == "kargo" && i.Note == "tamam"),
            "yonetici", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AgentClose_PassesTheLinkedAgent_SoItsLoadIsReleased()
    {
        (await ClientFor("Agent", "jane").PostAsJsonAsync("/agent/chat-sessions/s-2/close", Body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.Closing.Received(1).CloseAsync("s-2", Arg.Any<ConversationClosingInput>(), "jane", "agent-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_Is400_NotLive_Is404()
    {
        var admin = ClientFor("Admin");

        var invalid = await admin.PostAsJsonAsync("/chat-sessions/gecersiz/close", Body, Ct);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be("Kapanış nedeni seçin.");
        (await admin.PostAsJsonAsync("/chat-sessions/bot-modda/close", Body, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Options_ForAdminAndAgent()
    {
        foreach (var (role, url) in new[] { ("Admin", "/conversation-closing/options"), ("Agent", "/agent/conversation-closing/options") })
        {
            var json = await ClientFor(role).GetFromJsonAsync<JsonElement>(url, Ct);
            json.GetProperty("requireReason").GetBoolean().Should().BeTrue();
            json.GetProperty("reasons")[0].GetProperty("label").GetString().Should().Be("Çözüldü");
            json.GetProperty("suggestedTags")[0].GetString().Should().Be("kargo");
        }
    }

    [Fact]
    public async Task Customer_And_Anonymous_AreRejected()
    {
        (await ClientFor("Customer").PostAsJsonAsync("/agent/chat-sessions/s-3/close", Body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Agent").PostAsJsonAsync("/chat-sessions/s-3/close", Body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/conversation-closing/options", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
