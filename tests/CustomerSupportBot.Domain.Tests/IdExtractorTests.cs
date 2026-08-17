// Tests/Services/IdExtractorTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class IdExtractorTests
{
    [Fact]
    public void Extract_EmptyText_NoIds()
    {
        var ids = IdExtractor.Extract("");
        ids.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_OrderContext_ExtractsOrderId()
    {
        var ids = IdExtractor.Extract("sipariş 1030 nerede?");
        ids.OrderId.Should().Be("1030");
        ids.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Extract_ComplaintContext_ExtractsComplaintId()
    {
        var ids = IdExtractor.Extract("şikayet 1001 hakkında bilgi");
        ids.ComplaintId.Should().Be("1001");
    }

    [Fact]
    public void Extract_CustomerContext_ExtractsCustomerId()
    {
        var ids = IdExtractor.Extract("müşteri 1008 son siparişi");
        ids.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void Extract_ShortQuery_NumericTreatedAsCustomerId()
    {
        var ids = IdExtractor.Extract("1008");
        ids.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void Extract_LongSentenceNoContext_ReturnsNoIds()
    {
        var ids = IdExtractor.Extract(
            "2025 yılında bir işlem yaptım ve hala bekliyorum ne yapabilirim acaba?");
        ids.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_OrderAndCustomerInSameText_BothExtracted()
    {
        var ids = IdExtractor.Extract("sipariş 1030 müşteri 1027 bilgisi");
        ids.OrderId.Should().Be("1030");
        ids.CustomerId.Should().Be("1027");
    }

    [Fact]
    public void Extract_NumberInShortOrderQuery_ExtractsOrderId()
    {
        var ids = IdExtractor.Extract("sipariş 1042 iptal");
        ids.OrderId.Should().Be("1042");
        ids.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Extract_NoFourDigitNumber_ReturnsNoIds()
    {
        var ids = IdExtractor.Extract("sipariş numaram var ama hatırlamıyorum");
        ids.HasAny.Should().BeFalse();
    }

    [Theory]
    [InlineData("siparişim 1030 nerede?")]
    [InlineData("siparişimin durumu ne 1030")]
    [InlineData("siparişimi 1030 iptal etmek istiyorum")]
    public void Extract_SuffixedOrderKeyword_StillExtractsOrderId(string text)
    {
        // Türkçe eklemeli yapı ("sipariş" + iyelik eki) — \b...\b eskiden
        // eki geldiğinde eşleşmeyi kaçırıyordu (bkz. IdExtractor.cs yorum).
        var ids = IdExtractor.Extract(text);
        ids.OrderId.Should().Be("1030");
    }

    [Fact]
    public void Extract_SuffixedCustomerKeyword_StillExtractsCustomerId()
    {
        var ids = IdExtractor.Extract("müşterim 1008 son siparişi");
        ids.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void Extract_TwoConsecutiveOrderQueries_EachExtractsOwnOrderId()
    {
        // Gerçek kullanıcı senaryosu: "1042 nolu sipariş durumu" (bare kelime, eski
        // regex ile de çalışırdı) ardından aynı sohbette "1043 numaralı siparişin
        // durumu" (iyelik ekli — eski \b...\b regex burada kaçırıyordu, order_id
        // customer_id'ye düşüyor ya da hiç çıkarılmıyordu).
        var first = IdExtractor.Extract("1042 nolu sipariş durumunu sorgulamak istiyorum");
        first.OrderId.Should().Be("1042");

        var second = IdExtractor.Extract("1043 numaralı siparişin durumunu sorgulamak istiyorum");
        second.OrderId.Should().Be("1043");
        second.CustomerId.Should().BeNull();
    }

    [Fact]
    public void BuildHintMessage_NoIds_ReturnsNull()
    {
        IdExtractor.BuildHintMessage(new ExtractedIds()).Should().BeNull();
    }

    [Fact]
    public void BuildHintMessage_OrderId_GeneratesPriorityRule()
    {
        var hint = IdExtractor.BuildHintMessage(new ExtractedIds { OrderId = "1030" });
        hint.Should().NotBeNull();
        hint.Should().Contain("1030");
        hint.Should().Contain("order_status_tool");
    }

    [Fact]
    public void BuildHintMessage_OnlyCustomerId_GeneratesLastOrderHint()
    {
        var hint = IdExtractor.BuildHintMessage(new ExtractedIds { CustomerId = "1008" });
        hint.Should().NotBeNull();
        hint.Should().Contain("get_last_order_tool");
    }

    // ─── "numaram" sahiplenmesi — canlıda gözlemlenen regresyon ────────────────────
    // "Sipariş numaram 1041" = "benim SİPARİŞ numaram". Mesafe tabanlı seçimde "numaram"
    // sayıya "sipariş"ten daha yakın olduğu için (boşluk 1'e karşı 9) sayı customer_id
    // sanılıyordu. Etkisi yalnızca prompt hint'i değildi: SessionStateExtractor bu sonucu
    // KALICI session state'ine yazıp (state.CustomerId) sonraki tüm turlarda EntityVerifier
    // ve CustomerContextProvider'a yanlış müşteri kimliği besliyordu.

    [Theory]
    [InlineData("Sipariş numaram 1041.")]
    [InlineData("Sipariş numaram 1041")]
    [InlineData("siparis numaram 1041")]
    [InlineData("siparişimin numaram 1041")]
    public void Extract_OrderQualifiedNumaram_IsOrderIdNotCustomerId(string text)
    {
        var r = IdExtractor.Extract(text);
        r.OrderId.Should().Be("1041");
        r.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Extract_ComplaintQualifiedNumaram_IsComplaintIdNotCustomerId()
    {
        var r = IdExtractor.Extract("şikayet numaram 1001");
        r.ComplaintId.Should().Be("1001");
        r.CustomerId.Should().BeNull();
    }

    [Theory]
    [InlineData("müşteri numaram 1025")]
    [InlineData("numaram 1025")]          // niteleyicisiz → müşteri varsayımı korunur
    public void Extract_UnqualifiedOrCustomerNumaram_StaysCustomerId(string text)
    {
        var r = IdExtractor.Extract(text);
        r.CustomerId.Should().Be("1025");
        r.OrderId.Should().BeNull();
    }

    // ─── IsCustomerIdAssumed — EntityVerifier'ın bağlam-devamlılığı kararını beslediği bayrak ────

    [Fact]
    public void Extract_BareNumberNoContext_MarksCustomerIdAsAssumed()
    {
        // "1043" tek başına — hiçbir bağlam kelimesi yok. Bu, EntityVerifier'ın konuşma
        // geçmişinde daha güçlü bir sinyal (ör. önceki turda sipariş sorgusu) varsa
        // geçersiz kılabileceği ZAYIF bir tahmindir.
        var r = IdExtractor.Extract("1043");
        r.CustomerId.Should().Be("1043");
        r.IsCustomerIdAssumed.Should().BeTrue();
    }

    [Fact]
    public void Extract_ExplicitCustomerKeyword_IsNotAssumed()
    {
        // "müşteri" kelimesi gerçek bir sinyaldir — varsayım değil, EntityVerifier bunu
        // asla geçersiz kılmamalı.
        var r = IdExtractor.Extract("müşteri 1008 son siparişi");
        r.CustomerId.Should().Be("1008");
        r.IsCustomerIdAssumed.Should().BeFalse();
    }

    [Fact]
    public void Extract_OrderContext_DoesNotSetCustomerAssumedFlag()
    {
        var r = IdExtractor.Extract("sipariş 1030 nerede?");
        r.IsCustomerIdAssumed.Should().BeFalse();
    }

    // ─── FindLastUnambiguousKind / ApplyContextContinuity ──────────────────────────
    // EntityVerifier ve SessionStateExtractor'ın paylaştığı Domain-katmanı bağlam takibi.

    [Fact]
    public void FindLastUnambiguousKind_NoTexts_ReturnsNull()
    {
        IdExtractor.FindLastUnambiguousKind(null).Should().BeNull();
        IdExtractor.FindLastUnambiguousKind(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void FindLastUnambiguousKind_OrderKeyword_ReturnsOrder()
    {
        IdExtractor.FindLastUnambiguousKind(["sipariş numaram 1030"])
            .Should().Be(IdExtractor.ExtractedKind.Order);
    }

    [Fact]
    public void FindLastUnambiguousKind_ComplaintKeyword_ReturnsComplaint()
    {
        IdExtractor.FindLastUnambiguousKind(["şikayet numaram 1001"])
            .Should().Be(IdExtractor.ExtractedKind.Complaint);
    }

    [Fact]
    public void FindLastUnambiguousKind_AmbiguousBareNumberOnly_ReturnsNull()
    {
        // Kendisi de bağlamsız bir varsayımsa bu, bağlam KURMAZ.
        IdExtractor.FindLastUnambiguousKind(["1008"]).Should().BeNull();
    }

    [Fact]
    public void FindLastUnambiguousKind_SkipsAmbiguousEntries_UntilRealContextFound()
    {
        // En yeniden en eskiye taranır — ilk metin bağlamsız, ikincisi sipariş bağlamlı.
        IdExtractor.FindLastUnambiguousKind(["1043", "sipariş numaram 1030"])
            .Should().Be(IdExtractor.ExtractedKind.Order);
    }

    [Fact]
    public void ApplyContextContinuity_AssumedCustomerId_ReclassifiesAsOrder()
    {
        var ids = IdExtractor.Extract("1030");
        ids.IsCustomerIdAssumed.Should().BeTrue();

        IdExtractor.ApplyContextContinuity(ids, ["sipariş numaram 1030"]);

        ids.OrderId.Should().Be("1030");
        ids.CustomerId.Should().BeNull();
        ids.IsCustomerIdAssumed.Should().BeFalse();
    }

    [Fact]
    public void ApplyContextContinuity_NoPriorContext_LeavesCustomerIdUnchanged()
    {
        var ids = IdExtractor.Extract("1030");

        IdExtractor.ApplyContextContinuity(ids, null);

        ids.CustomerId.Should().Be("1030");
        ids.OrderId.Should().BeNull();
    }

    [Fact]
    public void ApplyContextContinuity_NotAssumed_IsNoOpEvenWithOrderContext()
    {
        // "müşteri" ile açıkça belirtilmiş bir customer_id, bağlam ne olursa olsun
        // yeniden sınıflandırılmamalı.
        var ids = IdExtractor.Extract("müşteri 1008 bilgisi");

        IdExtractor.ApplyContextContinuity(ids, ["sipariş numaram 1030"]);

        ids.CustomerId.Should().Be("1008");
        ids.OrderId.Should().BeNull();
    }
}
