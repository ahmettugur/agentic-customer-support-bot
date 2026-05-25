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
}
