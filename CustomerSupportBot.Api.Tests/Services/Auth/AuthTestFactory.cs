// Tests/Services/Auth/AuthTestFactory.cs
// Auth testleri için EF Core InMemory DbContextFactory kurar.
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Models.Auth;
using CustomerSupportBot.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Auth;

internal sealed class TestDbContextFactory : IDbContextFactory<CustomerSupportDbContext>
{
    private readonly DbContextOptions<CustomerSupportDbContext> _opts;
    public TestDbContextFactory(string dbName)
    {
        _opts = new DbContextOptionsBuilder<CustomerSupportDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public CustomerSupportDbContext CreateDbContext() => new(_opts);
    public Task<CustomerSupportDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(CreateDbContext());
}

internal static class AuthTestFactory
{
    public static (TokenService tokens, UserService users, IPasswordHasher hasher, TestDbContextFactory dbf) Build(
        JwtOptions? jwt = null)
    {
        jwt ??= new JwtOptions
        {
            Issuer = "test-iss",
            Audience = "test-aud",
            SigningKey = "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14
        };

        var dbf = new TestDbContextFactory($"auth-{Guid.NewGuid():N}");
        var hasher = new BCryptPasswordHasher();
        var tokens = new TokenService(
            dbf, Options.Create(jwt), NullLogger<TokenService>.Instance);
        var users = new UserService(
            dbf, hasher, NullLogger<UserService>.Instance);

        return (tokens, users, hasher, dbf);
    }
}
