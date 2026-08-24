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
    public void Verify_EmptyQuery_NothingExtracted()
    {
        var result = _verifier.Verify("", EmptySession());
        result.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Verify_OrderId_IsResolvedWithoutClaimingDbExistence()
    {
        var result = _verifier.Verify("sipariş 1030 nerede?", SessionOf("1027"));
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Source.Should().Be(EntitySource.Query);
        result.OrderId.Attributes.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Verify_UnknownOrderId_IsAlsoDeferredToOwnedTool()
    {
        var result = _verifier.Verify("sipariş 9999 nerede?", SessionOf("1027"));
        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Attributes.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Verify_AuthenticatedCustomer_IsTrustedWithoutDerivedBusinessData()
    {
        // Kimlik JWT'den gelir; sipariş bilgisi ise ihtiyaç olduğunda tool'dan okunur.
        var result = _verifier.Verify("son siparişim?", SessionOf("1008"));
        result.CustomerId.Should().NotBeNull();
        result.CustomerId!.Verification.Should().Be(EntityVerification.Verified);
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

    /// <summary>
    /// Geçmişte geçen müşteri numarası kimlik olarak KULLANILMAZ.
    ///
    /// <para>
    /// Bu test eskiden bunun tersini doğruluyordu (<c>Source == History</c>). Geçmiş,
    /// kullanıcının kendi yazdığı metinden oluşur; oradan kimlik almak, "müşteri numaram 1008"
    /// yazan herkesin 1008 olması demekti. Kimlik yalnızca JWT'den gelir.
    /// </para>
    /// </summary>
    [Fact]
    public void Verify_CustomerIdInHistory_IsNotUsedAsIdentity()
    {
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "müşteri numaram 1008")
        };

        var result = _verifier.Verify("siparişim?", SessionOf("1027"), history);

        result.CustomerId!.Value.Should().Be("1027");
        result.CustomerId.Source.Should().Be(EntitySource.SessionState);
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

        var result = _verifier.Verify(followUp, SessionOf("1027"), history);

        result.OrderId.Should().NotBeNull();
        result.OrderId!.Value.Should().Be("1030");
        result.OrderId.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Source.Should().Be(EntitySource.Query);
        result.CustomerId!.Value.Should().Be("1027",
            "sorgudaki 1030 sipariş olarak yorumlandı; kimlik ise her hâlükârda JWT'den gelir");
    }

    [Fact]
    public void Verify_AmbiguousFollowUp_AfterComplaintContext_ResolvesAsComplaint()
    {
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "şikayet numaram 1001")
        };

        var result = _verifier.Verify("peki 1001", SessionOf("1008"), history);

        result.ComplaintId.Should().NotBeNull();
        result.ComplaintId!.Value.Should().Be("1001");
        result.CustomerId!.Value.Should().Be("1008", "kimlik sorgudan değil JWT'den gelir");
    }

    /// <summary>
    /// Bağlamsız bir sayı SİPARİŞ sanılmamalı.
    ///
    /// <para>
    /// Test eskiden sayının <c>CustomerId</c> olarak yorumlandığını doğruluyordu; kimlik artık
    /// yalnızca JWT'den geldiği için o beklenti anlamını yitirdi. Korunan asıl değer şu: sayı
    /// siparişe kaymamalı — kaysaydı, kullanıcının yazdığı rastgele bir numara başkasının
    /// siparişini sorgulama girişimine dönüşürdü.
    /// </para>
    /// </summary>
    [Fact]
    public void Verify_AmbiguousQuery_NoHistory_IsNotTreatedAsOrder()
    {
        var result = _verifier.Verify("1008", SessionOf("1027"));

        result.OrderId.Should().BeNull();
        result.CustomerId!.Value.Should().Be("1027");
    }

    [Fact]
    public void Verify_AmbiguousFollowUp_PriorContextItselfAmbiguous_IsNotTreatedAsOrder()
    {
        // Önceki tur da bağlamsız bir varsayımdı (kendisi "numaram"sız bir sayı) — bu bağlam
        // kurmaz, dolayısıyla bu tur da siparişe kaymamalı.
        var history = new List<ConversationMessage> { new(ConversationRoles.User, "1008") };

        var result = _verifier.Verify("1027", SessionOf("1008"), history);

        result.OrderId.Should().BeNull();
        result.CustomerId!.Value.Should().Be("1008", "kimlik JWT'den; sorgudaki 1027 değil");
    }

    [Fact]
    public void Verify_ExplicitCustomerQuery_NeverReclassifiedByHistory()
    {
        // "müşteri" kelimesi açık bir sinyaldir — geçmişte sipariş bağlamı olsa bile
        // bu tur AÇIKÇA müşteri sorgusu; reclassification tetiklenmemeli.
        //
        // Sınıflandırma korumasının asıl değeri artık şurada: 1008 SİPARİŞ sanılırsa,
        // başkasının siparişi sorgulanmaya çalışılır. Kimlik tarafı ise JWT'den gelir —
        // sorgudaki numara kimliği değiştiremez (bkz. sahiplik testleri).
        var history = new List<ConversationMessage> { new(ConversationRoles.User, "sipariş numaram 1030") };

        var result = _verifier.Verify("müşteri 1008 bilgisi", SessionOf("1027"), history);

        // Geçmişten gelen 1030 sipariş olarak taşınır; ölçülen şey 1008'in ONA kaymamasıdır.
        result.OrderId?.Value.Should().NotBe("1008", "1008 açıkça müşteri bağlamında");
        result.CustomerId!.Value.Should().Be("1027");
    }

    // ─── Aynı numaranın hem sipariş hem şikayet olarak yorumlanması ─────────────────
    // Aynı sayı iki ayrı bağlamda geçerse resolver DB'ye bakıp adaylardan birini gerçek ilan
    // etmez. İki aday da tool katmanına kadar doğrulanmamış olarak korunur.

    [Fact]
    public void Verify_SameNumberAsOrderAndComplaint_KeepsBothAsUnverifiedCandidates()
    {
        var result = _verifier.Verify("sipariş 1030 ile ilgili şikayet 1030", SessionOf("1027"));

        result.OrderId.Should().NotBeNull();
        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.ComplaintId.Should().NotBeNull();
        result.ComplaintId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Attributes.Should().BeNullOrEmpty();
        result.ComplaintId.Attributes.Should().BeNullOrEmpty();
    }

    // ═══ Sahiplik — müşteriler arası veri sızıntısı ═══

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

    /// <summary>
    /// Resolver siparişe hiç bakmadığı için başka müşterinin kaydından attribute sızdıramaz.
    /// Aynı ID, gerçekliği ve sahipliği tool'da doğrulanmak üzere FormatOnly kalır.
    /// </summary>
    [Fact]
    public void Verify_ForeignOrderId_IsNotVerified_AndLeaksNoAttributes()
    {
        var session = EmptySession();
        session.State.AuthenticatedCustomerId = "9999";   // 1030 numaralı sipariş 1027'ye ait

        var result = _verifier.Verify("sipariş 1030 nerede?", session);

        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Attributes.Should().BeNullOrEmpty("başka müşterinin sipariş içeriği sızmamalı");
    }

    /// <summary>
    /// Kendi siparişi de resolver'da gerçek ilan edilmez; ID specialist'e taşınır ve tool doğrular.
    /// </summary>
    [Fact]
    public void Verify_OwnOrderId_IsPassedToToolWithoutAttributes()
    {
        var session = EmptySession();
        session.State.AuthenticatedCustomerId = "1027";

        var result = _verifier.Verify("sipariş 1030 nerede?", session);

        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Value.Should().Be("1030");
        result.OrderId.Attributes.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Kimlik yokken hiçbir sipariş doğrulanmaz. FormatOnly kalır; kayıt gerçekten yokmuş gibi
    /// konuşulmaz ve tool da authenticated müşteri olmadan iş verisi döndürmez.
    /// </summary>
    [Fact]
    public void Verify_WithoutAuthenticatedIdentity_VerifiesNothing()
    {
        var result = _verifier.Verify("sipariş 1030 nerede?", EmptySession());

        result.OrderId!.Verification.Should().Be(EntityVerification.FormatOnly);
        result.OrderId.Attributes.Should().BeNullOrEmpty();
        result.CustomerId.Should().BeNull();
        result.DerivedLastOrderId.Should().BeNullOrEmpty();
    }
}
