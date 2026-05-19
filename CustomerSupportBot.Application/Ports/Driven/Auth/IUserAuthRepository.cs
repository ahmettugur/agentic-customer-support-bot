// Application/Ports/Driven/Auth/IUserAuthRepository.cs
// Secondary port — user authentication persistence.

using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Driven.Auth;

public interface IUserAuthRepository
{
    Task<UserInfo?> FindActiveByUsernameAsync(string username, CancellationToken ct = default);
    Task<UserInfo?> FindByIdAsync(string id, CancellationToken ct = default);
    Task UpdateLastLoginAsync(string id, DateTime lastLoginAt, CancellationToken ct = default);
}
