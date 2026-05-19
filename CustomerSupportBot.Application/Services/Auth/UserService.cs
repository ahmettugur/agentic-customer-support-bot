using CustomerSupportBot.Application.Ports.Driven.Auth;
using CustomerSupportBot.Application.Ports.Driving.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Auth;

public sealed class UserService : IUserService
{
    private readonly IUserAuthRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserAuthRepository users,
        IPasswordHasher hasher,
        ILogger<UserService> logger)
    {
        _users = users;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<UserInfo?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _users.FindActiveByUsernameAsync(username, ct);

        if (user is null)
        {
            _logger.LogWarning("[Auth] Login başarısız: kullanıcı yok veya pasif. username={Username}", username);
            return null;
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("[Auth] Login başarısız: şifre yanlış. username={Username}", username);
            return null;
        }

        return user;
    }
}
