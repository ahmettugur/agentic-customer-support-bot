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
    public void Verify_CustomerFromSession_UsesAuthenticatedCustomerId()
    {
        var session = EmptySession();
        session.State.AuthenticatedCustomerId = "1008";

        var result = _verifier.Verify("siparişlerim?", session);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Source.Should().Be(EntitySource.SessionState);
    }

    [Fact]
    public void Verify_PoisonedSessionStateCustomerId_IsIgnored()
    {
        // State.CustomerId LLM'in serbest metinden çıkardığı, kullanıcı tarafından
        // zehirlenebilir bir alan — SessionState fallback'i yalnızca JWT'den gelen
        // AuthenticatedCustomerId'yi kullanmalı, yoksa bir kullanıcı "ben 1008 numaralı
        // müşteriyim" diyerek başkasının last_order_id/order_count türetilmiş alanlarını
        // reasoning prompt'una sızdırabilir.
        var session = EmptySession();
        session.State.CustomerId = "1008";
        session.State.AuthenticatedCustomerId = null;

        var result = _verifier.Verify("siparişlerim?", session);

        result.CustomerId.Should().BeNull();
        result.DerivedLastOrderId.Should().BeNullOrEmpty();
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

    // ─── Bağlamsız sayının önceki tur bağlamını takip etmesi ────────────────────────
    // Canlıda gözlemlenen senaryo: "sipariş numaram 1030" turundan sonra kullanıcı
    // sadece "peki 1030" (ya da tek başına "1030") yazınca IdExtractor bağlam bulamadığı
    // için 1030'u customer_id sanıyordu (1030 müşteri olarak DB'de yok → "bulunamadı"
    // yanıtı, oysa sipariş 1030 gerçekten mevcuttu). DB'ye "hangi tabloda var" diye
    // sormak yerine (order/customer/complaint aynı sayı aralığını paylaşabilir)
    // konuşmanın bağlamı takip edilir.

    [Theory]
    [InlineData("1030")]
    [InlineData("peki 1030")]
    public void Verify_AmbiguousFollowUp_AfterOrderContext_ResolvesAsOrder(string followUp)
    {
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "sipariş numaram 1030"),
            new(ConversationRoles.Assistant, "1030 numaralı siparişinizi kontrol ettim: Teslim Edildi.")
        };

        var result = _verifier.Verify(followUp, EmptySession(), history);

        result.OrderId.Should().NotBeNull();
        result.OrderId!.Value.Should().Be("1030");
        result.OrderId.Verification.Should().Be(EntityVerification.Verified);
        result.OrderId.Source.Should().Be(EntitySource.Query);
        result.CustomerId.Should().BeNull("1030 sipariş bağlamında yorumlanmalı, müşteriye kaymamalı");
    }

    [Fact]
    public void Verify_AmbiguousFollowUp_AfterComplaintContext_ResolvesAsComplaint()
    {
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "şikayet numaram 1001")
        };

        var result = _verifier.Verify("peki 1001", EmptySession(), history);

        result.ComplaintId.Should().NotBeNull();
        result.ComplaintId!.Value.Should().Be("1001");
        result.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Verify_AmbiguousQuery_NoHistory_StillDefaultsToCustomerId()
    {
        // Geriye dönük uyumluluk: gerçekten bağlam yoksa (ilk mesaj) eski davranış korunur.
        var result = _verifier.Verify("1008", EmptySession());

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Value.Should().Be("1008");
        result.OrderId.Should().BeNull();
    }

    [Fact]
    public void Verify_AmbiguousFollowUp_PriorContextItselfAmbiguous_StillDefaultsToCustomerId()
    {
        // Önceki tur da bağlamsız bir varsayımdı (kendisi "numaram"sız bir sayı) — bu bağlam
        // kurmaz, iki tur üst üste customer_id varsayımında kalınmalı.
        var history = new List<ConversationMessage> { new(ConversationRoles.User, "1008") };

        var result = _verifier.Verify("1027", EmptySession(), history);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Value.Should().Be("1027");
        result.OrderId.Should().BeNull();
    }

    [Fact]
    public void Verify_ExplicitCustomerQuery_NeverReclassifiedByHistory()
    {
        // "müşteri" kelimesi açık bir sinyaldir — geçmişte sipariş bağlamı olsa bile
        // bu tur AÇIKÇA müşteri sorgusu; reclassification tetiklenmemeli.
        var history = new List<ConversationMessage> { new(ConversationRoles.User, "sipariş numaram 1030") };

        var result = _verifier.Verify("müşteri 1008 bilgisi", EmptySession(), history);

        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Value.Should().Be("1008");
    }

    // ─── Aynı numaranın hem sipariş hem şikayet olarak yorumlanması ─────────────────
    // Aynı sayı iki numara olarak metinde geçip biri sipariş biri şikayet bağlamında
    // yorumlanırsa ve sipariş tarafı DB'de doğrulanmışsa, şikayet yorumu (ki DB'de
    // bulunamayacaktır) sahte bir "bulunamadı" uyarısına yol açmasın diye düşürülür.

    [Fact]
    public void Verify_SameNumberAsOrderAndComplaint_OrderVerified_DropsComplaintInterpretation()
    {
        var result = _verifier.Verify("sipariş 1030 ile ilgili şikayet 1030", EmptySession());

        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.Verified);
        result.ComplaintId.Should().BeNull("1030 gerçek bir şikayet kaydı değil, sahte uyarı üretmemeli");
    }
}
