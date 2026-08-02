// Tests/Services/IdExtractorTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Api.Tests.Services;

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
}
