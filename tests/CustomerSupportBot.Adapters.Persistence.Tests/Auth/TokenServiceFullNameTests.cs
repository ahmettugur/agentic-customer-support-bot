// Tests/Services/Auth/TokenServiceFullNameTests.cs
// Login yanıtındaki FullName — arayüzün kullanıcıya adıyla hitap edebilmesi için.
// Username e-posta olduğundan gösterime uygun tek alan budur. Ad, JWT'nin bağlı olduğu
// hesabın LinkedCustomerId'sinden çözülür; istekten gelen hiçbir değerden değil.

using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests.Auth;

[Collection("PostgresCatalog")]
public class TokenServiceFullNameTests(PostgresCatalogFixture fixture)
{
    private static readonly JwtOptions Jwt = new()
    {
        Issuer = "test-iss",
        Audience = "test-aud",
        SigningKey = "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14
    };

    /// <param name="dbFactory">
    /// Gerçek bir veritabanı gerekiyorsa (bkz. TryRevokeAsync/ExecuteUpdateAsync notu,
    /// AuthTestFactory.cs) — verilmezse izole InMemory kullanılır.
    /// </param>
    private static (TokenPortService Tokens, ICustomerRepository Customers, IDbContextFactory<CustomerSupportDbContext> Db) Build(
        IDbContextFactory<CustomerSupportDbContext>? dbFactory = null)
    {
        var opts = Options.Create(Jwt);
        var dbf = dbFactory ?? new TestDbContextFactory($"fullname-{Guid.NewGuid():N}");
        var customers = Substitute.For<ICustomerRepository>();

        var tokens = new TokenPortService(
            new EfUserAuthRepository(dbf),
            new EfRefreshTokenRepository(dbf),
            new JwtAccessTokenProvider(opts),
            opts,
            customers);

        return (tokens, customers, dbf);
    }

    /// <summary>
    /// Kullanıcıyı DB'ye yazar — IssueAsync içindeki UpdateLastLoginAsync kaydı Single() ile
    /// okuduğu için persist edilmemiş bir UserInfo ile token üretilemez.
    /// </summary>
    private static UserInfo Seed(
        IDbContextFactory<CustomerSupportDbContext> dbf, string role, string? linkedCustomerId, string username)
    {
        using var ctx = dbf.CreateDbContext();
        var entity = new CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth.UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = "irrelevant-for-token-issue",
            Role = role,
            LinkedCustomerId = linkedCustomerId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Users.Add(entity);
        ctx.SaveChanges();
        return new UserInfo(entity.Id, entity.Username, entity.PasswordHash, entity.Role,
            entity.LinkedAgentId, entity.IsActive, entity.CreatedAt, entity.LastLoginAt,
            entity.LinkedCustomerId);
    }

    private static UserInfo SeedCustomer(
        IDbContextFactory<CustomerSupportDbContext> dbf, string linkedCustomerId, string? username = null)
        => Seed(dbf, "Customer", linkedCustomerId, username ?? "ahmet.tugur@example.com");

    private static UserInfo SeedStaff(IDbContextFactory<CustomerSupportDbContext> dbf)
        => Seed(dbf, "Admin", null, "admin");

    [Fact]
    public async Task CustomerLogin_ReturnsFullNameFromCatalog()
    {
        var (tokens, customers, dbf) = Build();
        customers.GetFullNameAsync(1027, Arg.Any<CancellationToken>()).Returns("Ahmet Tügür");

        var auth = await tokens.IssueAsync(SeedCustomer(dbf, "1027"));

        auth.FullName.Should().Be("Ahmet Tügür");
        auth.Username.Should().Be("ahmet.tugur@example.com", "Username e-posta olarak kalmalı");
    }

    [Fact]
    public async Task StaffLogin_HasNoFullName_AndNeverQueriesCatalog()
    {
        var (tokens, customers, dbf) = Build();

        var auth = await tokens.IssueAsync(SeedStaff(dbf));

        auth.FullName.Should().BeNull();
        await customers.DidNotReceive().GetFullNameAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CustomerNotInCatalog_LoginStillSucceedsWithNullName()
    {
        // Ad yalnızca gösterim amaçlı — katalogda kayıt yoksa login akışı bozulmamalı.
        var (tokens, customers, dbf) = Build();
        customers.GetFullNameAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var auth = await tokens.IssueAsync(SeedCustomer(dbf, "9999"));

        auth.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.FullName.Should().BeNull();
    }

    [Fact]
    public async Task MalformedLinkedCustomerId_DoesNotThrow()
    {
        var (tokens, customers, dbf) = Build();

        var auth = await tokens.IssueAsync(SeedCustomer(dbf, "abc"));

        auth.FullName.Should().BeNull();
        await customers.DidNotReceive().GetFullNameAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_AlsoCarriesFullName()
    {
        // Sayfa yenilendiğinde token refresh ediliyor; ad refresh yolunda düşerse başlık
        // ilk girişte doğru görünüp sonra sessizce nötr karşılamaya dönerdi.
        // TryRevokeAsync ExecuteUpdateAsync kullanır — gerçek veritabanı gerekir.
        var (tokens, customers, dbf) = Build(dbFactory: fixture.DbFactory);
        customers.GetFullNameAsync(1027, Arg.Any<CancellationToken>()).Returns("Ahmet Tügür");
        var user = SeedCustomer(dbf, "1027", username: $"ahmet-{Guid.NewGuid():N}@example.com");

        var issued = await tokens.IssueAsync(user);
        var refreshed = await tokens.RefreshAsync(issued.RefreshToken);

        refreshed.Should().NotBeNull();
        refreshed!.FullName.Should().Be("Ahmet Tügür");
    }
}
