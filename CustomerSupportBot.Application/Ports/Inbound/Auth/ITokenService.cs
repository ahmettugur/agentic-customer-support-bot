using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Inbound.Auth;

public interface ITokenService
{
    Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default);
    Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default);
}
