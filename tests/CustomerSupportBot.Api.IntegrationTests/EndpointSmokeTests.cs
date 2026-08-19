// Tests/Endpoints/EndpointSmokeTests.cs
using System.Net;
using System.Net.Http.Json;

namespace CustomerSupportBot.Api.IntegrationTests;

/// <summary>
/// In-process integration tests using <see cref="TestWebApplicationFactory"/>.
/// InMemory persistence (no Postgres) + dummy AI config. Admin endpoints
/// require JWT → smoke tests verify 401 without token.
/// </summary>
public class EndpointSmokeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EndpointSmokeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() => _factory.CreateClient();

    // ─── Sessions: anonim erişim YOK ───
    //
    // Bu üç uç eskiden anonimdi ve testleri bunu "OK" olarak doğruluyordu. /sessions/ tüm
    // oturum kimliklerini listeliyor, diğer ikisi de o kimliklerle konuşmaları ve state'i
    // veriyordu — yani /chat/* üzerindeki müşteri yetkilendirmesi bu yoldan dolaşılabiliyordu.
    [Theory]
    [InlineData("/sessions/")]
    [InlineData("/sessions/unknown-session/messages")]
    [InlineData("/sessions/unknown-xyz/state")]
    public async Task SessionEndpoints_WithoutToken_Unauthorized(string path)
    {
        var resp = await NewClient().GetAsync(path, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Admin-only (no token → 401) ───
    [Theory]
    [InlineData("/traces/recent")]
    [InlineData("/traces/no-such")]
    [InlineData("/traces/stats")]
    [InlineData("/traces/sessions")]
    [InlineData("/approvals/pending")]
    [InlineData("/approvals/recent")]
    [InlineData("/approvals/no-such-id")]
    [InlineData("/escalations/open")]
    [InlineData("/escalations/recent")]
    [InlineData("/escalations/no-such-id")]
    [InlineData("/chat-sessions/active")]
    [InlineData("/analytics/dashboard")]
    [InlineData("/analytics/ratings/recent")]
    public async Task AdminEndpoints_WithoutToken_Unauthorized(string path)
    {
        var resp = await NewClient().GetAsync(path, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Auth endpoints (login is anonymous) ───
    [Fact]
    public async Task Post_AuthLogin_InvalidCreds_Unauthorized()
    {
        var resp = await NewClient().PostAsJsonAsync("/auth/login", new { Username = "no-such-user", Password = "wrong" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_AuthRefresh_InvalidToken_Unauthorized()
    {
        var resp = await NewClient().PostAsJsonAsync("/auth/refresh", new { RefreshToken = "not-a-real-token" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_AuthLogout_WithoutToken_Unauthorized()
    {
        var resp = await NewClient().PostAsJsonAsync("/auth/logout", new { RefreshToken = "x" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
