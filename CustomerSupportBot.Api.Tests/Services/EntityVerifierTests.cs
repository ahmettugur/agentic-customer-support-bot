// Tests/Services/EntityVerifierTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Api.Models.SessionState;

namespace CustomerSupportBot.Tests.Services;

public class EntityVerifierTests
{
    private readonly EntityVerifier _verifier = new(NullLogger<EntityVerifier>.Instance);

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
        var result = _verifier.Verify("ORD-1 nerede?", EmptySession());
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.Verified);
        result.OrderId.Source.Should().Be(EntitySource.Query);
    }

    [Fact]
    public void Verify_UnknownOrderId_NotFoundInDb()
    {
        var result = _verifier.Verify("ORD-9999 nerede?", EmptySession());
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.NotFoundInDb);
    }

    [Fact]
    public void Verify_KnownCustomerWithVerified_DerivesLastOrder()
    {
        var result = _verifier.Verify("CUST-1990 son siparişim?", EmptySession());
        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Verification.Should().Be(EntityVerification.Verified);
        result.DerivedLastOrderId.Should().NotBeNullOrEmpty();
        result.DerivedOrderCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Verify_CustomerFromSession_UsesSessionState()
    {
        var session = EmptySession();
        session.State.CustomerId = "CUST-1990";

        var result = _verifier.Verify("siparişlerim?", session);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Source.Should().Be(EntitySource.SessionState);
    }

    [Fact]
    public void Verify_CustomerFromHistory_UsesHistory()
    {
        var session = EmptySession();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "ben CUST-1990 müşteriyim")
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
                Value = "ORD-1",
                Verification = EntityVerification.Verified,
                Source = EntitySource.Query
            }
        };
        var block = EntityVerifier.BuildPromptBlock(verified);
        block.Should().Contain("ORD-1");
        block.Should().Contain("VERIFIED");
    }

    [Fact]
    public void BuildPromptBlock_NotFoundInDb_AddsWarning()
    {
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "ORD-9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        var block = EntityVerifier.BuildPromptBlock(verified);
        block.Should().Contain("DB'de bulunamadı");
    }
}
