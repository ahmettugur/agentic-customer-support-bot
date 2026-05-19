// Tests/Services/Auth/TokenServiceTests.cs

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Auth;

public class TokenServiceTests
{
    private static UserEntity Seed(TestDbContextFactory dbf, string username = "alice", bool active = true)
    {
        using var ctx = dbf.CreateDbContext();
        var user = new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = "irrelevant-for-token-issue",
            Role = "Admin",
            IsActive = active,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    [Fact]
    public void Ctor_ShortSigningKey_Throws()
    {
        var opts = Options.Create(new JwtOptions { SigningKey = "too-short" });
        var dbf = new TestDbContextFactory($"k-{Guid.NewGuid():N}");
        Action act = () => _ = new TokenService(dbf, opts, NullLogger<TokenService>.Instance);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ctor_EmptySigningKey_Throws()
    {
        var opts = Options.Create(new JwtOptions { SigningKey = "" });
        var dbf = new TestDbContextFactory($"k-{Guid.NewGuid():N}");
        Action act = () => _ = new TokenService(dbf, opts, NullLogger<TokenService>.Instance);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task IssueAsync_ReturnsTokens_AndPersistsRefresh_AndUpdatesLastLogin()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);

        var resp = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        resp.AccessToken.Should().NotBeNullOrEmpty();
        resp.RefreshToken.Should().NotBeNullOrEmpty();
        resp.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        resp.RefreshTokenExpiresAt.Should().BeAfter(resp.AccessTokenExpiresAt);
        resp.Username.Should().Be(user.Username);
        resp.Role.Should().Be("Admin");

        await using var ctx = dbf.CreateDbContext();
        (await ctx.RefreshTokens.CountAsync(t => t.UserId == user.Id, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        var reloaded = await ctx.Users.FirstAsync(u => u.Id == user.Id, cancellationToken: TestContext.Current.CancellationToken);
        reloaded.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndRevokesOld()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        var second = await tokens.RefreshAsync(first.RefreshToken, TestContext.Current.CancellationToken);

        second.Should().NotBeNull();
        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.AccessToken.Should().NotBeNullOrEmpty();

        using var ctx = await dbf.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var all = await ctx.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        all.Should().HaveCount(2);
        var oldOne = all.Single(t => t.RevokedAt is not null);
        oldOne.ReplacedByTokenHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ReturnsNull()
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        var resp = await tokens.RefreshAsync("does-not-exist", TestContext.Current.CancellationToken);
        resp.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefreshAsync_NullOrWhitespace_ReturnsNull(string? token)
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        var resp = await tokens.RefreshAsync(token!, TestContext.Current.CancellationToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        (await tokens.RevokeAsync(first.RefreshToken, TestContext.Current.CancellationToken)).Should().BeTrue();

        var resp = await tokens.RefreshAsync(first.RefreshToken, TestContext.Current.CancellationToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_InactiveUser_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        // User'� pasifle�tir
        await using (var ctx = await dbf.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var u = await ctx.Users.FirstAsync(x => x.Id == user.Id, cancellationToken: TestContext.Current.CancellationToken);
            u.IsActive = false;
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var resp = await tokens.RefreshAsync(first.RefreshToken, TestContext.Current.CancellationToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        await using (var ctx = await dbf.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var t = await ctx.RefreshTokens.FirstAsync(x => x.UserId == user.Id, cancellationToken: TestContext.Current.CancellationToken);
            t.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var resp = await tokens.RefreshAsync(first.RefreshToken, TestContext.Current.CancellationToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_ReturnsFalse()
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        (await tokens.RevokeAsync("nope", TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RevokeAsync_NullOrWhitespace_ReturnsFalse(string? token)
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        (await tokens.RevokeAsync(token!, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_ReturnsFalse()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user, TestContext.Current.CancellationToken);

        (await tokens.RevokeAsync(first.RefreshToken, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await tokens.RevokeAsync(first.RefreshToken, TestContext.Current.CancellationToken)).Should().BeFalse();
    }
}
