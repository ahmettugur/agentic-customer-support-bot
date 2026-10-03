// Refresh token YENİDEN KULLANIM TESPİTİ (rotation reuse detection).
//
// Rotasyonla iptal edilmiş bir refresh token'ın tekrar sunulması, token'ın kopyalandığının
// işaretidir: meşru kullanıcı ile saldırgandan hangisinin önce yenilediği bilinemez. Eskiden
// bu istek yalnızca reddediliyordu; saldırgan önce yenilediyse elde ettiği zincir sınırsızca
// yaşamaya devam ediyordu. Artık kullanıcının tüm aktif refresh token'ları iptal edilir.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class TokenPortServiceReuseDetectionTests
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

    private static (TokenPortService Service, InMemoryRefreshTokenRepository Tokens) Build()
    {
        var users = Substitute.For<IUserAuthRepository>();
        users.FindByIdAsync(User.Id, Arg.Any<CancellationToken>()).Returns(User);

        var jwt = Substitute.For<IJwtAccessTokenProvider>();
        jwt.GenerateAccessToken(Arg.Any<UserInfo>(), Arg.Any<DateTime>(), Arg.Any<int?>())
            .Returns((Token: "access-token", ExpiresAt: DateTime.UtcNow.AddMinutes(30)));

        var tokens = new InMemoryRefreshTokenRepository();
        return (new TokenPortService(users, tokens, jwt, Options.Create(JwtOpts)), tokens);
    }

    private static string Hash(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));

    /// <summary>İptal anını grace süresinin gerisine çeker — "token dakikalar sonra tekrar geldi".</summary>
    private static async Task BackdateRevocationAsync(IRefreshTokenRepository tokens, string plain)
    {
        var info = (await tokens.FindByHashAsync(Hash(plain)))!;
        await tokens.RevokeAsync(info.Id,
            DateTime.UtcNow - TokenPortService.ReuseGracePeriod - TimeSpan.FromMinutes(1),
            info.ReplacedByTokenHash);
    }

    [Fact]
    public async Task RotatedTokenReplayedAfterGrace_RevokesEveryActiveToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var (svc, tokens) = Build();
        var original = (await svc.IssueAsync(User, ct)).RefreshToken;
        var otherDevice = (await svc.IssueAsync(User, ct)).RefreshToken;

        var rotated = (await svc.RefreshAsync(original, ct))!.RefreshToken;
        await BackdateRevocationAsync(tokens, original);

        (await svc.RefreshAsync(original, ct)).Should().BeNull();

        (await svc.RefreshAsync(rotated, ct)).Should().BeNull(
            "kopyalanmış zincirin devamı da iptal edilmeli — hangisinin meşru olduğu bilinmiyor");
        (await svc.RefreshAsync(otherDevice, ct)).Should().BeNull(
            "token ailesi izlenmediği için kullanıcının tüm oturumları kapatılır");
    }

    [Fact]
    public async Task RotatedTokenReplayedWithinGrace_IsOnlyRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (svc, _) = Build();
        var original = (await svc.IssueAsync(User, ct)).RefreshToken;

        var rotated = (await svc.RefreshAsync(original, ct))!.RefreshToken;

        // Aynı localStorage'ı paylaşan ikinci sekme aynı anda yeniledi — meşru yarış.
        (await svc.RefreshAsync(original, ct)).Should().BeNull();
        (await svc.RefreshAsync(rotated, ct)).Should().NotBeNull(
            "kazanan sekmenin yeni zinciri yaşamaya devam etmeli");
    }

    [Fact]
    public async Task LoggedOutTokenReplayed_DoesNotRevokeOtherSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var (svc, tokens) = Build();
        var loggedOut = (await svc.IssueAsync(User, ct)).RefreshToken;
        var otherDevice = (await svc.IssueAsync(User, ct)).RefreshToken;

        (await svc.RevokeAsync(loggedOut, ct)).Should().BeTrue();
        await BackdateRevocationAsync(tokens, loggedOut);

        (await svc.RefreshAsync(loggedOut, ct)).Should().BeNull();
        (await svc.RefreshAsync(otherDevice, ct)).Should().NotBeNull(
            "logout ile iptal edilmiş token'ın yerine yenisi verilmedi — kopyalanma işareti değil");
    }

    [Fact]
    public async Task Logout_RacingWithRotation_DoesNotEraseRotationRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var (svc, tokens) = Build();
        var original = (await svc.IssueAsync(User, ct)).RefreshToken;

        await svc.RefreshAsync(original, ct);
        (await svc.RevokeAsync(original, ct)).Should().BeFalse("token zaten rotasyonla iptal edildi");

        (await tokens.FindByHashAsync(Hash(original), ct))!.ReplacedByTokenHash.Should().NotBeNull(
            "rotasyon kaydı silinirse sonraki yeniden kullanım tespit edilemez");
    }
}
