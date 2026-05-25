// Tests/Services/EntityVerifierTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Api.Tests.Services;

[Collection("PostgresCatalog")]
public class EntityVerifierTests
{
    private readonly EntityVerifier _verifier;

    public EntityVerifierTests(PostgresCatalogFixture fixture)
    {
        _verifier = new EntityVerifier(
            fixture.OrderRepo,
            fixture.ComplaintRepo,
            NullLogger<EntityVerifier>.Instance);
    }

    private static AgentSession EmptySession() => new()
    {
        SessionId = "s1",
        State = new SessionState()
    };

    [Fact]
    public void Verify_EmptyQuery_NothingExtracted()
    {
        var result = _verifier.Verify("", EmptySession());
        result.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Verify_KnownOrderId_VerifiedFromDb()
    {
        var result = _verifier.Verify("sipariş 1030 nerede?", EmptySession());
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.Verified);
        result.OrderId.Source.Should().Be(EntitySource.Query);
    }

    [Fact]
    public void Verify_UnknownOrderId_NotFoundInDb()
    {
        var result = _verifier.Verify("sipariş 9999 nerede?", EmptySession());
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.NotFoundInDb);
    }

    [Fact]
    public void Verify_KnownCustomerWithVerified_DerivesLastOrder()
    {
        var result = _verifier.Verify("müşteri 1008 son siparişim?", EmptySession());
        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Verification.Should().Be(EntityVerification.Verified);
        result.DerivedLastOrderId.Should().NotBeNullOrEmpty();
        result.DerivedOrderCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Verify_CustomerFromSession_UsesSessionState()
    {
        var session = EmptySession();
        session.State.CustomerId = "1008";

        var result = _verifier.Verify("siparişlerim?", session);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Source.Should().Be(EntitySource.SessionState);
    }

    [Fact]
    public void Verify_CustomerFromHistory_UsesHistory()
    {
        var session = EmptySession();
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "müşteri numaram 1008")
        };

        var result = _verifier.Verify("siparişim?", session, history);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Source.Should().Be(EntitySource.History);
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
