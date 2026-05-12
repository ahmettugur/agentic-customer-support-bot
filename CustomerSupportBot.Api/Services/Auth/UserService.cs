// Services/Auth/IUserService.cs

using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Services.Auth;

public interface IUserService
{
    Task<UserEntity?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IPasswordHasher hasher,
        ILogger<UserService> logger)
    {
        _dbFactory = dbFactory;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<UserEntity?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var user = await ctx.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null || !user.IsActive)
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
