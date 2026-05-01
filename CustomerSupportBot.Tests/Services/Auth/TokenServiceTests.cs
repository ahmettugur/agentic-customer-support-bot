// Tests/Services/Auth/TokenServiceTests.cs
using CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Models.Auth;
using CustomerSupportBot.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Tests.Services.Auth;

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

        var resp = await tokens.IssueAsync(user);

        resp.AccessToken.Should().NotBeNullOrEmpty();
        resp.RefreshToken.Should().NotBeNullOrEmpty();
        resp.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        resp.RefreshTokenExpiresAt.Should().BeAfter(resp.AccessTokenExpiresAt);
        resp.Username.Should().Be(user.Username);
        resp.Role.Should().Be("Admin");

        using var ctx = dbf.CreateDbContext();
        (await ctx.RefreshTokens.CountAsync(t => t.UserId == user.Id)).Should().Be(1);
        var reloaded = await ctx.Users.FirstAsync(u => u.Id == user.Id);
        reloaded.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndRevokesOld()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user);

        var second = await tokens.RefreshAsync(first.RefreshToken);

        second.Should().NotBeNull();
        second!.RefreshToken.Should().NotBe(first.RefreshToken);
        second.AccessToken.Should().NotBeNullOrEmpty();

        using var ctx = dbf.CreateDbContext();
        var all = await ctx.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        all.Should().HaveCount(2);
        var oldOne = all.Single(t => t.RevokedAt is not null);
        oldOne.ReplacedByTokenHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ReturnsNull()
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        var resp = await tokens.RefreshAsync("does-not-exist");
        resp.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefreshAsync_NullOrWhitespace_ReturnsNull(string? token)
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        var resp = await tokens.RefreshAsync(token!);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user);

        (await tokens.RevokeAsync(first.RefreshToken)).Should().BeTrue();

        var resp = await tokens.RefreshAsync(first.RefreshToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_InactiveUser_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user);

        // User'ı pasifleştir
        using (var ctx = dbf.CreateDbContext())
        {
            var u = await ctx.Users.FirstAsync(x => x.Id == user.Id);
            u.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        var resp = await tokens.RefreshAsync(first.RefreshToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user);

        using (var ctx = dbf.CreateDbContext())
        {
            var t = await ctx.RefreshTokens.FirstAsync(x => x.UserId == user.Id);
            t.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await ctx.SaveChangesAsync();
        }

        var resp = await tokens.RefreshAsync(first.RefreshToken);
        resp.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_ReturnsFalse()
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        (await tokens.RevokeAsync("nope")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RevokeAsync_NullOrWhitespace_ReturnsFalse(string? token)
    {
        var (tokens, _, _, _) = AuthTestFactory.Build();
        (await tokens.RevokeAsync(token!)).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_ReturnsFalse()
    {
        var (tokens, _, _, dbf) = AuthTestFactory.Build();
        var user = Seed(dbf);
        var first = await tokens.IssueAsync(user);

        (await tokens.RevokeAsync(first.RefreshToken)).Should().BeTrue();
        (await tokens.RevokeAsync(first.RefreshToken)).Should().BeFalse();
    }
}
