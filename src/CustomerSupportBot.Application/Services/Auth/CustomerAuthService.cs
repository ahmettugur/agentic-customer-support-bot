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

        // KİMLİK SAHİPLİĞİ. Müşterinin VAR OLMASI, kaydolanın O MÜŞTERİ OLDUĞU anlamına gelmez.
        // Bu kontrol olmadan uç anonim olduğu için herkes başkasının müşteri numarasıyla hesap
        // açıp o müşteri adına geçerli bir JWT alabiliyordu — ve sistemin geri kalanındaki tüm
        // sahiplik kontrolleri (EntityVerifier, oturum sahipliği, tool sahiplik kuralları) o
        // token'ı doğru müşteri sanıp geçiriyordu.
        //
        // SINIR: bu bir e-posta DOĞRULAMASI değil, eşleşmesidir. Müşterinin kayıtlı e-postasını
        // bilen biri, gerçek sahip henüz kaydolmadıysa hesabı açabilir. Ancak hesabı O e-posta
        // ile açmak zorundadır; kendi adresine bağlayamaz ve gerçek sahip aynı adresle giriş
        // denediğinde durumu fark eder. Tam çözüm e-posta/OTP doğrulamasıdır ve bu kontrol
        // onun doğal ön adımıdır.
        if (!await _customers.IsEmailOwnedByCustomerAsync(customerIdLong, email, ct))
        {
            _logger.LogWarning(
                "[Auth] Müşteri kaydı reddedildi — e-posta müşteri kaydıyla eşleşmiyor. "
              + "email={Email} customerId={CustomerId}", email, customerId);

            // Hata mesajı hangi alanın yanlış olduğunu SÖYLEMEZ: ayrım verilseydi, geçerli
            // müşteri numaraları ile kayıtlı e-postalar deneme yanılmayla eşleştirilebilirdi.
            return (null, "E-posta adresi ile müşteri kimlik numarası eşleşmiyor.");
        }

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
