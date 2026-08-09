// Application/Ports/Driven/Auth/IUserAuthRepository.cs
// Secondary port — user authentication persistence.

using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Outbound.Auth;

public interface IUserAuthRepository
{
    Task<UserInfo?> FindActiveByUsernameAsync(string username, CancellationToken ct = default);
    Task<UserInfo?> FindByIdAsync(string id, CancellationToken ct = default);
    Task UpdateLastLoginAsync(string id, DateTime lastLoginAt, CancellationToken ct = default);

    /// <summary>
    /// Yeni bir kullanıcı hesabı oluşturur (müşteri self-servis kaydı için). Username zaten
    /// alınmışsa null döner — çağıran taraf (ör. CustomerAuthService) bunu "e-posta kullanımda"
    /// olarak yorumlar.
    /// </summary>
    Task<UserInfo?> CreateAsync(
        string username,
        string passwordHash,
        string role,
        string? linkedCustomerId,
        CancellationToken ct = default);
}
