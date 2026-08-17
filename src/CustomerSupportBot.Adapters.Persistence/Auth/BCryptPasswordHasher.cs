// Auth/BCryptPasswordHasher.cs
// IPasswordHasher'ın BCrypt.Net implementasyonu.
// WorkFactor=11 — üretim ortamı için yeterli maliyet, test ortamında düşürülebilir.

using CustomerSupportBot.Application.Ports.Outbound.Auth;

namespace CustomerSupportBot.Adapters.Persistence.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
