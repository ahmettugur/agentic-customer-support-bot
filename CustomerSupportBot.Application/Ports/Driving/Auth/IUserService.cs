using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Driving.Auth;

public interface IUserService
{
    Task<UserInfo?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}
