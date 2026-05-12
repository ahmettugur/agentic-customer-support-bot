// Tests/Services/Auth/UserServiceTests.cs

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Api.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services.Auth;

public class UserServiceTests
{
    private static (UserService svc, TestDbContextFactory dbf, IPasswordHasher hasher) Build()
    {
        var dbf = new TestDbContextFactory($"u-{Guid.NewGuid():N}");
        var hasher = new BCryptPasswordHasher();
        var svc = new UserService(dbf, hasher, NullLogger<UserService>.Instance);
        return (svc, dbf, hasher);
    }

    private static UserEntity SeedUser(TestDbContextFactory dbf, IPasswordHasher hasher,
        string username, string password, bool active = true)
    {
        using var ctx = dbf.CreateDbContext();
        var user = new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = "Admin",
            IsActive = active,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCreds_ReturnsUser()
    {
        var (svc, dbf, hasher) = Build();
        SeedUser(dbf, hasher, "alice", "MyPass123!");

        var result = await svc.AuthenticateAsync("alice", "MyPass123!", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Username.Should().Be("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_WrongPassword_ReturnsNull()
    {
        var (svc, dbf, hasher) = Build();
        SeedUser(dbf, hasher, "alice", "MyPass123!");

        var result = await svc.AuthenticateAsync("alice", "WrongPassword", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownUser_ReturnsNull()
    {
        var (svc, _, _) = Build();
        var result = await svc.AuthenticateAsync("ghost", "anything", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_InactiveUser_ReturnsNull()
    {
        var (svc, dbf, hasher) = Build();
        SeedUser(dbf, hasher, "alice", "MyPass123!", active: false);

        var result = await svc.AuthenticateAsync("alice", "MyPass123!", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("   ", "x")]
    [InlineData("alice", null)]
    [InlineData("alice", "")]
    [InlineData("alice", "   ")]
    public async Task AuthenticateAsync_NullOrWhitespace_ReturnsNull(string? user, string? pass)
    {
        var (svc, _, _) = Build();
        var result = await svc.AuthenticateAsync(user!, pass!, TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }
}
