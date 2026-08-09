// Application/Services/Auth/CustomerAuthService.cs
// ICustomerAuthService driving port implementasyonu — müşteri self-servis kayıt/login.
// Staff (Admin/Agent) auth'unun (UserService/TokenPortService) aynı users tablosunu ve JWT
// boru hattını kullanır — Role="Customer" ile ayrışır, ayrı bir tablo/servis zinciri açmaz.

using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Auth;

public sealed class CustomerAuthService : ICustomerAuthService
{
    private const string CustomerRole = "Customer";

    private readonly IUserAuthRepository _users;
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<CustomerAuthService> _logger;

    public CustomerAuthService(
        IUserAuthRepository users,
        ICustomerRepository customers,
        IPasswordHasher hasher,
        ILogger<CustomerAuthService> logger)
    {
        _users = users;
        _customers = customers;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<(UserInfo? User, string? Error)> RegisterAsync(
        string email, string password, string customerId, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return (null, "E-posta ve şifre zorunludur.");
        if (password.Length < 8)
            return (null, "Şifre en az 8 karakter olmalıdır.");
        if (!long.TryParse(customerId, out var customerIdLong) || !_customers.Exists(customerIdLong))
            return (null, "Geçerli bir müşteri kimlik numarası girin.");

        var existing = await _users.FindActiveByUsernameAsync(email, ct);
        if (existing is not null)
            return (null, "Bu e-posta adresiyle zaten bir hesap var.");

        var created = await _users.CreateAsync(
            email, _hasher.Hash(password), CustomerRole, customerId, ct);

        if (created is null)
            return (null, "Bu e-posta adresiyle zaten bir hesap var.");

        _logger.LogInformation("[Auth] Yeni müşteri hesabı oluşturuldu. email={Email} customerId={CustomerId}",
            email, customerId);

        return (created, null);
    }

    public async Task<UserInfo?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _users.FindActiveByUsernameAsync(email, ct);
        if (user is null || user.Role != CustomerRole)
        {
            _logger.LogWarning("[Auth] Müşteri login başarısız: hesap yok/pasif. email={Email}", email);
            return null;
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("[Auth] Müşteri login başarısız: şifre yanlış. email={Email}", email);
            return null;
        }

        return user;
    }
}
