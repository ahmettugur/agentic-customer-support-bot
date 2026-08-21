// Refresh rotasyonunda KAYBEDEN TARAF.
//
// TryRevokeAsync koşullu bir sahiplenmedir (bkz. IRefreshTokenRepository). Bu test onun
// çağrıldığı yeri kilitler: RefreshAsync, iptal başarısız dönerse (token eşzamanlı bir
// çağrı tarafından zaten iptal edilmiş) YENİ TOKEN ÜRETMEMELİ. Aksi hâlde koşullu sahiplenme
// doğru yazılmış olsa bile, kaybeden çağıran onu görmezden gelip yine de bir token üretebilir
// — kazanım tamamen etkisiz kalır.

using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class TokenPortServiceRotationTests
{
    private static readonly JwtOptions JwtOpts = new()
    {
        Issuer = "test-iss", Audience = "test-aud",
        SigningKey = "TEST_SIGNING_KEY_AT_LEAST_32_CHARS_LONG_FOR_HMAC_SHA256_!",
        AccessTokenMinutes = 30, RefreshTokenDays = 14
    };

    private static readonly UserInfo User = new(
        Id: "u1", Username: "musteri", PasswordHash: "", Role: "Customer",
        LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null);

    private static RefreshTokenInfo ValidToken(string hash) => new(
        Id: "tok-1", UserId: User.Id, TokenHash: hash,
        ExpiresAt: DateTime.UtcNow.AddDays(1), CreatedAt: DateTime.UtcNow,
        RevokedAt: null, ReplacedByTokenHash: null);

    private static (TokenPortService Service, IRefreshTokenRepository Tokens) Build(bool tryRevokeSucceeds)
    {
        var users = Substitute.For<IUserAuthRepository>();
        users.FindByIdAsync(User.Id, Arg.Any<CancellationToken>()).Returns(User);

        var tokens = Substitute.For<IRefreshTokenRepository>();
        // Hash'i bilmemize gerek yok: FindByHashAsync herhangi bir girdi için aynı kaydı döner.
        tokens.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ValidToken("irrelevant"));
        tokens.TryRevokeAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(tryRevokeSucceeds);

        var jwt = Substitute.For<IJwtAccessTokenProvider>();
        jwt.GenerateAccessToken(Arg.Any<UserInfo>(), Arg.Any<DateTime>(), Arg.Any<int?>())
            .Returns((Token: "access-token", ExpiresAt: DateTime.UtcNow.AddMinutes(30)));
        return (new TokenPortService(users, tokens, jwt, Options.Create(JwtOpts)), tokens);
    }

    /// <summary>
    /// ASIL BULGU. TryRevokeAsync false dönerse (kaybeden taraf), RefreshAsync yeni bir
    /// token üretmeden null dönmeli.
    /// </summary>
    [Fact]
    public async Task WhenTryRevokeLoses_NoNewTokenIsIssued()
    {
        var (service, tokens) = Build(tryRevokeSucceeds: false);

        var result = await service.RefreshAsync("herhangi-bir-token", TestContext.Current.CancellationToken);

        result.Should().BeNull("koşullu sahiplenmeyi kaybeden çağıran yeni bir oturum zinciri başlatmamalı");
        await tokens.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Karşı yön: kazanan taraf normal şekilde yeni token almalı.</summary>
    [Fact]
    public async Task WhenTryRevokeWins_ANewTokenIsIssued()
    {
        var (service, tokens) = Build(tryRevokeSucceeds: true);

        var result = await service.RefreshAsync("herhangi-bir-token", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await tokens.Received(1).CreateAsync(
            Arg.Any<string>(), User.Id, Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
