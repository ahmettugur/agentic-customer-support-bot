// Tests/Services/IdExtractorTests.cs

using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services;
using FluentAssertions;

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
    public void Extract_OrderIdHyphen_Extracted()
    {
        var ids = IdExtractor.Extract("ORD-123 siparişimin durumu nedir?");
        ids.OrderId.Should().Be("ORD-123");
    }

    [Fact]
    public void Extract_OrderIdLowercase_NormalizedToUpper()
    {
        var ids = IdExtractor.Extract("ord-5 nerede?");
        ids.OrderId.Should().Be("ORD-5");
    }

    [Fact]
    public void Extract_ComplaintId_Extracted()
    {
        var ids = IdExtractor.Extract("CMP-42 hakkında bilgi");
        ids.ComplaintId.Should().Be("CMP-42");
    }

    [Fact]
    public void Extract_CustomerPrefixId_Extracted()
    {
        var ids = IdExtractor.Extract("CUST-1990 müşterisiyim");
        ids.CustomerId.Should().Be("CUST-1990");
    }

    [Fact]
    public void Extract_NumericInLongSentence_NotConfusedWithCustomerId()
    {
        var ids = IdExtractor.Extract(
            "2025 yılında bir sipariş verdim ve bu sipariş hala bana ulaşmadı, çok bekledim ne yapmalıyım?");
        ids.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Extract_ShortQuery_NumericTreatedAsCustomerId()
    {
        var ids = IdExtractor.Extract("12345");
        ids.CustomerId.Should().Be("12345");
    }

    [Fact]
    public void Extract_OrderIdAndCustomerId_BothExtracted()
    {
        var ids = IdExtractor.Extract("ORD-1 müşteri 1990");
        ids.OrderId.Should().Be("ORD-1");
        ids.CustomerId.Should().Be("1990");
    }

    [Fact]
    public void Extract_NumberInOrderIdNotMisreadAsCustomerId()
    {
        var ids = IdExtractor.Extract("ORD-12345 ne zaman gelir");
        ids.OrderId.Should().Be("ORD-12345");
        ids.CustomerId.Should().BeNull();
    }

    [Fact]
    public void BuildHintMessage_NoIds_ReturnsNull()
    {
        IdExtractor.BuildHintMessage(new ExtractedIds()).Should().BeNull();
    }

    [Fact]
    public void BuildHintMessage_OrderId_GeneratesPriorityRule()
    {
        var hint = IdExtractor.BuildHintMessage(new ExtractedIds { OrderId = "ORD-1" });
        hint.Should().NotBeNull();
        hint!.Should().Contain("ORD-1");
        hint.Should().Contain("order_status_tool");
    }

    [Fact]
    public void BuildHintMessage_OnlyCustomerId_GeneratesLastOrderHint()
    {
        var hint = IdExtractor.BuildHintMessage(new ExtractedIds { CustomerId = "CUST-1" });
        hint.Should().NotBeNull();
        hint!.Should().Contain("get_last_order_tool");
    }
}
