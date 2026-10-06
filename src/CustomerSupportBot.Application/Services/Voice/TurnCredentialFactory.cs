// TURN REST API (coturn use-auth-secret): kısa ömürlü kullanıcı adı/parola. Kalıcı sır tarayıcıya gitmez.

using System.Security.Cryptography;
using System.Text;

namespace CustomerSupportBot.Application.Services.Voice;

public static class TurnCredentialFactory
{
    public static (string Username, string Credential) Create(string sharedSecret, string callId, DateTimeOffset now, TimeSpan ttl)
    {
        var username = $"{now.Add(ttl).ToUnixTimeSeconds()}:{callId}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
        return (username, credential);
    }
}
