// Bot yanıtına 👍/👎: HTTP sınırı. Sahiplik, doğrulama, geçmişte oyun görünmesi ve admin okuma.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class MessageFeedbackEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public MessageFeedbackEndpointTests(TestWebApplicationFactory f) => _factory = f;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Password = "Pass#1234";
    private const string Answer = "Siparişiniz kargoda.";

    private async Task<HttpClient> LoginAsync(string username, string linkedCustomerId)
    {
        await using (var ctx = await _factory.GetDbContextFactory().CreateDbContextAsync(Ct))
        {
            ctx.Users.Add(new UserEntity
            {
                Id = Guid.NewGuid().ToString("N"), Username = username, PasswordHash = new BCryptPasswordHasher().Hash(Password),
                Role = "Customer", LinkedCustomerId = linkedCustomerId, IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync(Ct);
        }
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new { Username = username, Password }, Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await resp.Content.ReadFromJsonAsync<AuthResponse>(Ct))!.AccessToken);
        return client;
    }

    private HttpClient AdminClient()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(Guid.NewGuid().ToString("N"), "fb-admin", "", "Admin", null, true, DateTime.UtcNow, null, null);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    private async Task<string> SeedAsync(string customerId, string bot = Answer)
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();
        var session = await sessions.GetOrCreateAsync(null, CancellationToken.None);
        session.State.AuthenticatedCustomerId = customerId;
        await sessions.UpdateAsync(session, CancellationToken.None);
        await sessions.AddExchangeAsync(session.SessionId, "siparişim nerede", bot, ct: CancellationToken.None);
        return session.SessionId;
    }

    private static Task<HttpResponseMessage> Vote(HttpClient c, string sid, string text, bool? helpful, string? reason = null, string? comment = null) =>
        c.PostAsJsonAsync($"/sessions/{sid}/messages/feedback", new { text, helpful, reason, comment }, Ct);

    [Fact]
    public async Task Customer_VotesOnOwnBotMessage_AndSeesTheVoteWhenTheHistoryIsReloaded()
    {
        var sid = await SeedAsync("3101");
        var client = await LoginAsync("fb-owner", "3101");

        var resp = await Vote(client, sid, Answer, false, "wrong", "fiyat yanlış");
        var messages = await new ChatApiService(client).GetSessionMessagesAsync(sid);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        messages.Single(m => m.Role == "bot").Feedback.Should().BeEquivalentTo(new MessageFeedbackView(false, "wrong"));
        messages.Single(m => m.Role == "user").Feedback.Should().BeNull();
    }

    [Fact]
    public async Task ClearingTheVote_RemovesItFromTheHistory()
    {
        var sid = await SeedAsync("3102");
        var client = await LoginAsync("fb-clear", "3102");
        await Vote(client, sid, Answer, true);

        var resp = await Vote(client, sid, Answer, null);
        var messages = await new ChatApiService(client).GetSessionMessagesAsync(sid);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        messages.Single(m => m.Role == "bot").Feedback.Should().BeNull();
    }

    [Fact]
    public async Task ForeignSession_Is403_AndNothingIsStored()
    {
        var sid = await SeedAsync("3103");
        var intruder = await LoginAsync("fb-intruder", "3104");

        var resp = await Vote(intruder, sid, Answer, false, "wrong");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IMessageFeedbackStore>().GetForSessionAsync(sid, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/sessions/x/messages/feedback",
            new { text = "a", helpful = true }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InventedText_Is404()
    {
        var sid = await SeedAsync("3105");
        var client = await LoginAsync("fb-invent", "3105");

        (await Vote(client, sid, "hiç söylenmemiş", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("WRONG")]
    public async Task UnknownReason_Is400(string reason)
    {
        var sid = await SeedAsync("3106");
        var client = await LoginAsync("fb-reason-" + reason.Length, "3106");

        (await Vote(client, sid, Answer, false, reason)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminReadsRecentFeedback_WithoutTheBotText_AndCustomersCannot()
    {
        var sid = await SeedAsync("3107");
        var client = await LoginAsync("fb-admin-read", "3107");
        await Vote(client, sid, Answer, false, "irrelevant");

        var adminResp = await AdminClient().GetAsync("/analytics/message-feedback/recent?count=200&helpful=false", Ct);
        var customerResp = await client.GetAsync("/analytics/message-feedback/recent", Ct);

        adminResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await adminResp.Content.ReadAsStringAsync(Ct);
        var rows = JsonDocument.Parse(body).RootElement.EnumerateArray().Where(r => r.GetProperty("sessionId").GetString() == sid).ToList();
        rows.Should().ContainSingle();
        rows[0].GetProperty("reason").GetString().Should().Be("irrelevant");
        body.Should().NotContain(Answer, "bot metni geri bildirim listesinde dönmemeli");
        customerResp.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized]);
    }
}
