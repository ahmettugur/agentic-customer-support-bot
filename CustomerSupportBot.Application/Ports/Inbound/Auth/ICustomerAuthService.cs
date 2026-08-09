using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Inbound.Auth;

public interface ICustomerAuthService
{
    /// <summary>
    /// Yeni müşteri hesabı kaydı. E-posta zaten kullanımdaysa veya customerId
    /// (mevcut CustomerEntity) geçerli değilse null döner ve error açıklaması taşır.
    /// </summary>
    Task<(UserInfo? User, string? Error)> RegisterAsync(
        string email, string password, string customerId, CancellationToken ct = default);

    Task<UserInfo?> AuthenticateAsync(string email, string password, CancellationToken ct = default);
}
