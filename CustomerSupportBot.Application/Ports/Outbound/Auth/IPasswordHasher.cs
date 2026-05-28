namespace CustomerSupportBot.Application.Ports.Outbound.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
