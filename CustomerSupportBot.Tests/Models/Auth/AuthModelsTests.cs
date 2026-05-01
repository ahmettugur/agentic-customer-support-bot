// Tests/Models/Auth/AuthModelsTests.cs
using CustomerSupportBot.Models.Auth;

namespace CustomerSupportBot.Tests.Models.Auth;

public class JwtOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var o = new JwtOptions();
        o.Issuer.Should().Be("CustomerSupportBot");
        o.Audience.Should().Be("CustomerSupportBot");
        o.SigningKey.Should().BeEmpty();
        o.AccessTokenMinutes.Should().Be(30);
        o.RefreshTokenDays.Should().Be(14);
        JwtOptions.SectionName.Should().Be("Jwt");
    }
}

public class AuthDtosTests
{
    [Fact]
    public void LoginRequest_RoundTrip()
    {
        var r = new LoginRequest("alice", "secret");
        r.Username.Should().Be("alice");
        r.Password.Should().Be("secret");
    }

    [Fact]
    public void RefreshAndLogoutRequest_RoundTrip()
    {
        new RefreshRequest("rt").RefreshToken.Should().Be("rt");
        new LogoutRequest("rt2").RefreshToken.Should().Be("rt2");
    }

    [Fact]
    public void AuthResponse_StoresAllFields()
    {
        var ax = DateTime.UtcNow.AddMinutes(30);
        var rx = DateTime.UtcNow.AddDays(14);
        var r = new AuthResponse("a", "r", ax, rx, "alice", "Admin");

        r.AccessToken.Should().Be("a");
        r.RefreshToken.Should().Be("r");
        r.AccessTokenExpiresAt.Should().Be(ax);
        r.RefreshTokenExpiresAt.Should().Be(rx);
        r.Username.Should().Be("alice");
        r.Role.Should().Be("Admin");
    }
}
