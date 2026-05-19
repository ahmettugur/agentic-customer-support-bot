namespace CustomerSupportBot.Application.Ports.Driven.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
