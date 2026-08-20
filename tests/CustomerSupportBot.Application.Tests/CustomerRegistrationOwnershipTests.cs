// Public müşteri kaydında KİMLİK SAHİPLİĞİ.
//
// Uç anonimdir ve kayıt sonrası hemen JWT verir. Kontrol yalnızca "böyle bir müşteri var mı"
// olduğu sürece, herkes başkasının müşteri numarasıyla hesap açıp o müşteri adına GEÇERLİ bir
// token alabiliyordu. Bunun ağırlığı şurada: sistemin geri kalanındaki tüm sahiplik kontrolleri
// (EntityVerifier, oturum sahipliği, tool sahiplik kuralları) o token'ı doğru müşteri sanıp
// geçirir — yani tek bir kayıt çağrısı, üstteki bütün savunmaları anlamsız kılar.

using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Application.Tests;

public class CustomerRegistrationOwnershipTests
{
    private const string OwnerEmail = "maria.anders@example.com";
    private const long CustomerId = 1001;

    private static (CustomerAuthService Service, IUserAuthRepository Users) Build()
    {
        var customers = Substitute.For<ICustomerRepository>();
        customers.Exists(CustomerId).Returns(true);
        customers.IsEmailOwnedByCustomerAsync(CustomerId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => string.Equals(
                call.ArgAt<string>(1), OwnerEmail, StringComparison.OrdinalIgnoreCase));

        var users = Substitute.For<IUserAuthRepository>();
        users.FindActiveByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserInfo?)null);
        users.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserInfo(
                Id: "u1", Username: call.ArgAt<string>(0), PasswordHash: "", Role: "Customer",
                LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
                LinkedCustomerId: call.ArgAt<string?>(3)));

        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hash");

        return (new CustomerAuthService(users, customers, hasher,
            NullLogger<CustomerAuthService>.Instance), users);
    }

    /// <summary>
    /// ASIL AÇIK: saldırganın kendi e-postası + başkasının müşteri numarası.
    /// Müşteri gerçekten VAR, ama kaydolan o müşteri DEĞİL.
    /// </summary>
    [Fact]
    public async Task Register_WithSomeoneElsesCustomerId_IsRejected()
    {
        var (service, users) = Build();

        var (user, error) = await service.RegisterAsync(
            "saldirgan@example.com", "GucluParola1", CustomerId.ToString(),
            TestContext.Current.CancellationToken);

        user.Should().BeNull("var olan bir müşteri numarası, o müşteri OLMAK anlamına gelmez");
        error.Should().NotBeNullOrEmpty();
        await users.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Hata mesajı hangi alanın yanlış olduğunu SÖYLEMEMELİ: ayrım verilseydi, geçerli müşteri
    /// numaraları ile kayıtlı e-postalar deneme yanılmayla eşleştirilebilirdi.
    /// </summary>
    [Fact]
    public async Task Register_RejectionMessage_DoesNotRevealWhichFieldWasWrong()
    {
        var (service, _) = Build();

        var (_, error) = await service.RegisterAsync(
            "saldirgan@example.com", "GucluParola1", CustomerId.ToString(),
            TestContext.Current.CancellationToken);

        error.Should().NotContain("e-posta bulunamadı")
             .And.NotContain("müşteri bulunamadı");
    }

    /// <summary>Karşı yön: gerçek sahip kaydolabilmeli — koruma olağan akışı kapatmamalı.</summary>
    [Fact]
    public async Task Register_WithMatchingEmail_Succeeds()
    {
        var (service, _) = Build();

        var (user, error) = await service.RegisterAsync(
            OwnerEmail, "GucluParola1", CustomerId.ToString(),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        user!.LinkedCustomerId.Should().Be(CustomerId.ToString());
    }

    /// <summary>E-posta karşılaştırması büyük/küçük harfe takılmamalı.</summary>
    [Fact]
    public async Task Register_EmailMatchIsCaseInsensitive()
    {
        var (service, _) = Build();

        var (user, _) = await service.RegisterAsync(
            OwnerEmail.ToUpperInvariant(), "GucluParola1", CustomerId.ToString(),
            TestContext.Current.CancellationToken);

        user.Should().NotBeNull();
    }
}
