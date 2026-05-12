// Tests/Endpoints/AuthEndpointsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Api.Models.Auth;
using CustomerSupportBot.Api.Services.Auth;

namespace CustomerSupportBot.Tests.Endpoints;

public class AuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public AuthEndpointsTests(TestWebApplicationFactory f) => _factory = f;

    private async Task<UserEntity> SeedAdminAsync(string username, string password)
    {
        var dbf = _factory.GetDbContextFactory();
        await using var ctx = await dbf.CreateDbContextAsync();

        // Aynı username varsa kullan
        var existing = ctx.Users.FirstOrDefault(u => u.Username == username);
        if (existing is not null) return existing;

        var hasher = new BCryptPasswordHasher();
        var user = new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Login_ValidCreds_Returns200WithTokens()
    {
        await SeedAdminAsync("login-ok", "Pass#1234");
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/auth/login", new { Username = "login-ok", Password = "Pass#1234" }, cancellationToken: TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.Username.Should().Be("login-ok");
        body.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await SeedAdminAsync("login-bad", "Pass#1234");
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/auth/login", new { Username = "login-bad", Password = "WrongPass" }, cancellationToken: TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new { Username = "ghost-user", Password = "x" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ValidToken_RotatesAndReturns200()
    {
        await SeedAdminAsync("refresh-ok", "Pass#1234");
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new { Username = "refresh-ok", Password = "Pass#1234" }, cancellationToken: TestContext.Current.CancellationToken);
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var resp = await client.PostAsJsonAsync("/auth/refresh", new { RefreshToken = loginBody!.RefreshToken }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var newBody = await resp.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: TestContext.Current.CancellationToken);
        newBody!.RefreshToken.Should().NotBe(loginBody.RefreshToken);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/refresh", new { RefreshToken = "unknown-refresh" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutBearer_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/logout", new { RefreshToken = "any" }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithBearer_KnownToken_Returns204()
    {
        await SeedAdminAsync("logout-ok", "Pass#1234");
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new { Username = "logout-ok", Password = "Pass#1234" }, cancellationToken: TestContext.Current.CancellationToken);
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var resp = await client.PostAsJsonAsync("/auth/logout", new { RefreshToken = body.RefreshToken }, cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AdminEndpoint_WithValidBearer_Returns2xx()
    {
        await SeedAdminAsync("admin-flow", "Pass#1234");
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new { Username = "admin-flow", Password = "Pass#1234" }, cancellationToken: TestContext.Current.CancellationToken);
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var resp = await client.GetAsync("/traces/recent", TestContext.Current.CancellationToken);
        ((int)resp.StatusCode).Should().BeLessThan(400, "Admin role bearer ile yetkili olmalı");
    }
}
