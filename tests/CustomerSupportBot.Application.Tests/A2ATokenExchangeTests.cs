// Tests/A2ATokenExchangeTests.cs
//
// A2A kimlik katmanının GÜVENLİK sınırları. Bu kanal dış sistemlere açık olduğu için buradaki
// asıl soru "token üretiliyor mu" değil, "ÜRETİLMEMESİ GEREKEN durumda üretilmiyor mu".
//
// Kapı yanlış açılırsa bedeli başka bir müşterinin sipariş geçmişidir: partner token'ı ele
// geçiren biri herhangi bir müşteri numarasını isteyip o müşterinin verisine erişebilir.

using CustomerSupportBot.Application.Ports.Outbound.A2A;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class A2ATokenExchangeTests
{
    private static (A2ATokenExchangeService Svc, IJwtAccessTokenProvider Tokens, List<UserInfo> Issued)
        Build(A2AOptions options)
    {
        var issued = new List<UserInfo>();
        var tokens = Substitute.For<IJwtAccessTokenProvider>();
        tokens.GenerateAccessToken(Arg.Do<UserInfo>(u => issued.Add(u)), Arg.Any<DateTime>(), Arg.Any<int?>())
            .Returns(ci => ("tok", ci.ArgAt<DateTime>(1).AddMinutes(ci.ArgAt<int?>(2) ?? 60)));

        var opts = Options.Create(options);
        var authorizer = new ConfiguredA2ASubjectAuthorizer(
            opts, NullLogger<ConfiguredA2ASubjectAuthorizer>.Instance);

        var svc = new A2ATokenExchangeService(
            authorizer, tokens, opts, NullLogger<A2ATokenExchangeService>.Instance);

        return (svc, tokens, issued);
    }

    private static A2AOptions WithPartner(string partnerId, params string[] customers) => new()
    {
        Enabled = true,
        SubjectTokenMinutes = 5,
        Partners = [new A2APartnerOptions { PartnerId = partnerId, AllowedCustomerIds = customers.ToList() }]
    };

    // ═══ Varsayılan REDDETMEK olmalı ═══

    /// <summary>
    /// Hiç yapılandırma yoksa hiçbir token üretilmemeli. "Yapılandırmayı unuttum" durumunun
    /// sonucu sızıntı değil, çalışmama olmalıdır.
    /// </summary>
    [Fact]
    public async Task NoConfiguration_DeniesEverything()
    {
        var (svc, _, _) = Build(new A2AOptions());

        (await svc.ExchangeAsync("acme", "1027")).Should().BeNull();
    }

    [Fact]
    public async Task UnknownPartner_IsDenied()
    {
        var (svc, _, _) = Build(WithPartner("acme", "1027"));

        (await svc.ExchangeAsync("saldirgan", "1027")).Should().BeNull();
    }

    /// <summary>
    /// EN KRİTİK TEST: tanımlı bir partner, kendisine AÇILMAMIŞ bir müşteri adına token alamamalı.
    /// Bu kontrol düşerse partner token'ı tüm müşterilerin verisine açılır.
    /// </summary>
    [Fact]
    public async Task KnownPartner_CannotActForCustomerOutsideItsAllowlist()
    {
        var (svc, _, _) = Build(WithPartner("acme", "1027"));

        (await svc.ExchangeAsync("acme", "1001")).Should().BeNull("1001 bu partnere açılmadı");
    }

    [Theory]
    [InlineData("", "1027")]
    [InlineData("acme", "")]
    [InlineData(" ", " ")]
    public async Task BlankIdentifiers_AreDenied(string partnerId, string customerId)
    {
        var (svc, _, _) = Build(WithPartner("acme", "1027"));

        (await svc.ExchangeAsync(partnerId, customerId)).Should().BeNull();
    }

    // ═══ İzin verilen durum ═══

    [Fact]
    public async Task AllowedPartnerAndCustomer_GetsSubjectToken()
    {
        var (svc, _, issued) = Build(WithPartner("acme", "1027"));

        var result = await svc.ExchangeAsync("acme", "1027");

        result.Should().NotBeNull();
        result!.CustomerId.Should().Be("1027");
        issued.Should().ContainSingle();
    }

    /// <summary>
    /// Üretilen token TEK müşteriye kilitli olmalı: müşteri kimliği token'ın içinde
    /// (<c>linked_customer_id</c>'ye dönüşen alan) taşınır. Taşınmasaydı çağrı gövdesinde
    /// parametre olarak gitmesi gerekirdi — yani sohbet kanalında bilerek kapatılan açık
    /// (istemcinin müşteri kimliğini değiştirebilmesi) A2A'da yeniden açılırdı.
    /// </summary>
    [Fact]
    public async Task IssuedToken_IsLockedToTheRequestedCustomer()
    {
        var (svc, _, issued) = Build(WithPartner("acme", "1027"));

        await svc.ExchangeAsync("acme", "1027");

        issued.Single().LinkedCustomerId.Should().Be("1027");
    }

    /// <summary>
    /// Özne rolü, sohbet/sesli kanalın "Customer" rolünden FARKLI olmalı — bir kanalın token'ı
    /// diğerinde kullanılamasın.
    /// </summary>
    [Fact]
    public async Task IssuedToken_UsesDedicatedSubjectRole_NotCustomerRole()
    {
        var (svc, _, issued) = Build(WithPartner("acme", "1027"));

        await svc.ExchangeAsync("acme", "1027");

        issued.Single().Role.Should().Be(A2ARoles.Subject);
        issued.Single().Role.Should().NotBe("Customer");
    }

    /// <summary>Token kısa ömürlü olmalı — tek bir çağrı için yeterlidir.</summary>
    [Fact]
    public async Task IssuedToken_UsesShortConfiguredLifetime()
    {
        var opts = WithPartner("acme", "1027");
        opts.SubjectTokenMinutes = 3;
        var (svc, tokens, _) = Build(opts);

        await svc.ExchangeAsync("acme", "1027");

        tokens.Received(1).GenerateAccessToken(Arg.Any<UserInfo>(), Arg.Any<DateTime>(), 3);
    }

    /// <summary>Denetim izi: hangi partner adına üretildiği token kimliğinde görünmeli.</summary>
    [Fact]
    public async Task IssuedToken_CarriesPartnerIdForAudit()
    {
        var (svc, _, issued) = Build(WithPartner("acme", "1027"));

        await svc.ExchangeAsync("acme", "1027");

        issued.Single().Id.Should().Contain("acme").And.Contain("1027");
    }

    // ═══ Özne kimlik biçimi — rate limit bölümlemesi buna bağlı ═══
    //
    // Biçim iki yerde kullanılıyor: token üretilirken ve rate limit'te partner çıkarılırken.
    // Ayrışırsa hata GÖRÜNÜR bir arıza değil, sessizce kaybolan bir koruma olur: bölümleme
    // yanlış anahtara düşer ve partner başına sınır fiilen ortadan kalkar.

    [Fact]
    public void SubjectIdentity_RoundTrips_PartnerId()
    {
        var id = A2ASubjectIdentity.BuildId("acme", "1027");

        A2ASubjectIdentity.TryGetPartnerId(id).Should().Be("acme");
    }

    /// <summary>Üretilen GERÇEK token kimliğinden partner çıkarılabilmeli — iki uç bağlı kalmalı.</summary>
    [Fact]
    public async Task IssuedTokenId_YieldsPartnerId_ForRateLimitPartitioning()
    {
        var (svc, _, issued) = Build(WithPartner("acme", "1027"));

        await svc.ExchangeAsync("acme", "1027");

        A2ASubjectIdentity.TryGetPartnerId(issued.Single().Id).Should().Be("acme");
    }

    /// <summary>
    /// Beklenmedik biçimde null dönmeli — çağıran bunu "bilinmeyen partner" olarak ele alır.
    /// Tahmin etseydi, uydurulmuş bir anahtara bölümlenir ve sınır yanlış uygulanırdı.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rastgele")]
    [InlineData("a2a")]
    [InlineData("a2a:")]
    [InlineData("baska:acme:1027")]
    [InlineData("a2a::1027")]
    public void SubjectIdentity_MalformedInput_ReturnsNull(string? input)
    {
        A2ASubjectIdentity.TryGetPartnerId(input).Should().BeNull();
    }

    /// <summary>Farklı müşteriler için üretilen token'lar AYNI partner anahtarına düşmeli.</summary>
    [Fact]
    public void SubjectIdentity_DifferentCustomers_SamePartner_SharePartitionKey()
    {
        var a = A2ASubjectIdentity.TryGetPartnerId(A2ASubjectIdentity.BuildId("acme", "1027"));
        var b = A2ASubjectIdentity.TryGetPartnerId(A2ASubjectIdentity.BuildId("acme", "9999"));

        a.Should().Be(b, "bir partner çok sayıda müşteri adına çağrı yaparak sınırı aşamamalı");
    }

    // ═══ Wildcard — bilinçli ve riskli ═══

    [Fact]
    public async Task WildcardPartner_CanActForAnyCustomer()
    {
        var (svc, _, _) = Build(WithPartner("trusted", "*"));

        (await svc.ExchangeAsync("trusted", "9999")).Should().NotBeNull();
    }

    /// <summary>Wildcard yalnızca o partnere ait olmalı — başka partnere sızmamalı.</summary>
    [Fact]
    public async Task WildcardOfOnePartner_DoesNotLeakToAnother()
    {
        var opts = new A2AOptions
        {
            Enabled = true,
            Partners =
            [
                new A2APartnerOptions { PartnerId = "trusted", AllowedCustomerIds = ["*"] },
                new A2APartnerOptions { PartnerId = "limited", AllowedCustomerIds = ["1027"] }
            ]
        };
        var (svc, _, _) = Build(opts);

        (await svc.ExchangeAsync("limited", "1001")).Should().BeNull();
        (await svc.ExchangeAsync("trusted", "1001")).Should().NotBeNull();
    }
}
