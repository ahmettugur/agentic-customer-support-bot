// Tests/Services/Auth/AuthTestFactory.cs
// Auth testleri için EF Core InMemory altyapısı ve servis fabrikası.

using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Services.Auth;
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
    public static (ITokenService tokens, UserService users, IPasswordHasher hasher,
        TestDbContextFactory dbf, IUserAuthRepository userRepo, IRefreshTokenRepository tokenRepo) Build(
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

        var opts = Options.Create(jwt);
        var dbf = new TestDbContextFactory($"auth-{Guid.NewGuid():N}");
        var hasher = new BCryptPasswordHasher();
        var userRepo = new EfUserAuthRepository(dbf);
        var tokenRepo = new EfRefreshTokenRepository(dbf);
        var jwtProvider = new JwtAccessTokenProvider(opts);

        var tokens = new TokenPortService(userRepo, tokenRepo, jwtProvider, opts);
        var users = new UserService(userRepo, hasher, NullLogger<UserService>.Instance);

        return (tokens, users, hasher, dbf, userRepo, tokenRepo);
    }
}
