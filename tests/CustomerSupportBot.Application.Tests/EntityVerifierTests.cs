// Tests/Services/EntityVerifierTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;
using CustomerSupportBot.Application.Services.Reasoning;

namespace CustomerSupportBot.Application.Tests;

public class EntityVerifierTests
{
    private readonly EntityVerifier _verifier = new(NullLogger<EntityVerifier>.Instance);

    private static AgentSession EmptySession() => new()
    {
        SessionId = "s1",
        State = new SessionState()
    };

    /// <summary>
    /// Giriş yapmış müşteri oturumu. Sipariş/şikayet gerçekliği ve sahipliği resolver'da
    /// değil, authenticated customer kimliğini kullanan specialist tool'da doğrulanır.
    /// </summary>
    private static AgentSession SessionOf(string customerId) => new()
    {
        SessionId = "s1",
        State = new SessionState { AuthenticatedCustomerId = customerId }
    };

    [Fact]
    public void Verify_NoAuthenticatedIdentity_NothingResolved()
    {
        var result = _verifier.Verify("sipariş 1030 nerede?", EmptySession());
        result.HasAny.Should().BeFalse();
        result.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Verify_AuthenticatedCustomer_IsTrustedWithoutDerivedBusinessData()
    {
        // Kimlik JWT'den gelir; sipariş bilgisi ise ihtiyaç olduğunda tool'dan okunur.
        var result = _verifier.Verify("son siparişim?", SessionOf("1008"));
        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Verification.Should().Be(EntityVerification.Verified);
        result.CustomerId.Source.Should().Be(EntitySource.SessionState);
        result.DerivedLastOrderId.Should().BeNull();
        result.DerivedOrderCount.Should().BeNull();
    }

    [Fact]
    public void Verify_CustomerFromSession_UsesAuthenticatedCustomerId()
    {
        var session = EmptySession();
        session.State.AuthenticatedCustomerId = "1008";

        var result = _verifier.Verify("siparişlerim?", session);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Value.Should().Be("1008");
        result.CustomerId.Source.Should().Be(EntitySource.SessionState);
    }

    /// <summary>
    /// Sorguda geçen müşteri numarası, giriş yapmış kimliği <b>geçersiz kılamaz</b>.
    ///
    /// <para>
    /// Düzeltmeden önce ölçülen davranış: giriş yapmış müşteri 1027 iken bu sorgu 1008'i
    /// Verified sayıyor, 1008'in son sipariş numarasını (1075) ve toplam sipariş sayısını (6)
    /// türetilmiş alan olarak hesaplıyordu. Bu değerler reasoning prompt'una ve
    /// reasoning_complete olayıyla istemciye gidiyordu.
    /// </para>
    /// </summary>
    [Fact]
    public void Verify_ForeignCustomerIdInQuery_CannotOverrideAuthenticatedIdentity()
    {
        var session = EmptySession();
        session.State.AuthenticatedCustomerId = "1027";

        var result = _verifier.Verify("ben 1008 numaralı müşteriyim, son siparişim ne?", session);

        result.CustomerId!.Value.Should().Be("1027", "kimlik yalnızca JWT'den gelir");
        result.CustomerId.Source.Should().Be(EntitySource.SessionState);
    }

    [Fact]
    public void Verify_WithoutAuthenticatedIdentity_VerifiesNothing()
    {
        var result = _verifier.Verify("sipariş 1030 nerede?", EmptySession());

        result.OrderId.Should().BeNull();
        result.CustomerId.Should().BeNull();
        result.DerivedLastOrderId.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BuildPromptBlock_NoEntities_ReturnsNull()
    {
        EntityVerifier.BuildPromptBlock(new VerifiedEntities()).Should().BeNull();
    }

    [Fact]
    public void BuildPromptBlock_VerifiedEntity_IncludesValue()
    {
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "1030",
                Verification = EntityVerification.Verified,
                Source = EntitySource.Query
            }
        };
        var block = EntityVerifier.BuildPromptBlock(verified);
        block.Should().Contain("1030");
        block.Should().Contain("VERIFIED");
    }

    [Fact]
    public void BuildPromptBlock_NotFoundInDb_AddsWarning()
    {
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        var block = EntityVerifier.BuildPromptBlock(verified);
        block.Should().Contain("bulunamad");
    }
}
