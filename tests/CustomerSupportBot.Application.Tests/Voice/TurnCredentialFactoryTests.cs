// TURN REST API kimliği: coturn use-auth-secret ile aynı hesap (HMAC-SHA1, base64).

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Services.Voice;

namespace CustomerSupportBot.Application.Tests.Voice;

public class TurnCredentialFactoryTests
{
    [Fact]
    public void Credential_IsHmacSha1OfUsername_AndUsernameCarriesExpiry()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var (user, pass) = TurnCredentialFactory.Create("devturnsecret", "call-1", now, TimeSpan.FromMinutes(10));

        user.Should().Be($"{now.AddMinutes(10).ToUnixTimeSeconds()}:call-1");
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("devturnsecret"));
        pass.Should().Be(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(user))));
    }
}
