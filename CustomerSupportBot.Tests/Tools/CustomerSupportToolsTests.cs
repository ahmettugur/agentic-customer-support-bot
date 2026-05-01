using CustomerSupportBot.Models;
using CustomerSupportBot.Tools;

namespace CustomerSupportBot.Tests.Tools;

public class CustomerSupportToolsTests
{
    // ─── ProductInquiryTool ───
    [Fact]
    public void ProductInquiry_BlankName_ValidationError()
    {
        var r = CustomerSupportTools.ProductInquiryTool("");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void ProductInquiry_UnknownProduct_NotFound()
    {
        var r = CustomerSupportTools.ProductInquiryTool("ZZZ-yokboyle-urun-xyz");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
    }

    [Fact]
    public void ProductInquiry_KnownProduct_Ok()
    {
        var firstProduct = FakeDatabase.ProductCatalog.Keys.First();
        var r = CustomerSupportTools.ProductInquiryTool(firstProduct);
        r.Success.Should().BeTrue();
        r.Confidence.Should().Be(1.0);
        r.Message.Should().Contain(firstProduct);
    }

    // ─── OrderPlacementTool ───
    [Fact]
    public void OrderPlacement_AllMissing_ValidationError()
    {
        var r = CustomerSupportTools.OrderPlacementTool("", null, "");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderPlacement_UnknownProduct_NotFound()
    {
        var r = CustomerSupportTools.OrderPlacementTool("ZZZ-yokboyle", 1, "CUST-1990");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
    }

    [Fact]
    public void OrderPlacement_Valid_CreatesOrder()
    {
        var product = FakeDatabase.ProductCatalog.Keys.First();
        var beforeOrders = FakeDatabase.OrdersDb.Count;
        var r = CustomerSupportTools.OrderPlacementTool(product, 1, $"CUST-TEST-{Guid.NewGuid():N}");
        r.Success.Should().BeTrue();
        FakeDatabase.OrdersDb.Count.Should().BeGreaterThan(beforeOrders);
    }

    [Fact]
    public void OrderPlacement_DuplicateWithinWindow_ReturnsCachedResult()
    {
        var product = FakeDatabase.ProductCatalog.Keys.First();
        var customerId = $"CUST-IDEM-{Guid.NewGuid():N}";
        var r1 = CustomerSupportTools.OrderPlacementTool(product, 1, customerId);
        var r2 = CustomerSupportTools.OrderPlacementTool(product, 1, customerId);
        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        // Aynı orderId döner (idempotency)
        r1.Message.Should().Be(r2.Message);
    }

    [Fact]
    public void OrderPlacement_StockInsufficient_Conflict()
    {
        var product = FakeDatabase.ProductCatalog.Keys.First();
        var r = CustomerSupportTools.OrderPlacementTool(product, 999_999, $"CUST-X-{Guid.NewGuid():N}");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.StockInsufficient);
    }

    // ─── OrderStatusTool ───
    [Fact]
    public void OrderStatus_BlankId_ValidationError()
    {
        var r = CustomerSupportTools.OrderStatusTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderStatus_KnownOrder_Ok()
    {
        var r = CustomerSupportTools.OrderStatusTool("ORD-1");
        r.Success.Should().BeTrue();
        r.Message.Should().Contain("ORD-1");
    }

    [Fact]
    public void OrderStatus_UnknownOrder_NotFound()
    {
        var r = CustomerSupportTools.OrderStatusTool("ORD-9999999");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    // ─── ComplaintRegistrationTool ───
    [Fact]
    public void Complaint_MissingFields_ValidationError()
    {
        var r = CustomerSupportTools.ComplaintRegistrationTool("", "kısa", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_TooShortDescription_ValidationError()
    {
        var r = CustomerSupportTools.ComplaintRegistrationTool("ORD-1", "az", "CUST-1990");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_UnknownOrder_NotFound()
    {
        var r = CustomerSupportTools.ComplaintRegistrationTool(
            "ORD-99999", "şikayet açıklaması burada yer alır", "CUST-X");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    [Fact]
    public void Complaint_CustomerIdMismatch_Conflict()
    {
        var r = CustomerSupportTools.ComplaintRegistrationTool(
            "ORD-1", "ürün hatalı geldi paket açılmış", "CUST-WRONG-XYZ");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);
    }

    [Fact]
    public void Complaint_OmitCustomerId_Inferred()
    {
        var product = FakeDatabase.ProductCatalog.Keys.First();
        var customerId = $"CUST-COMPL-{Guid.NewGuid():N}";
        var orderResult = CustomerSupportTools.OrderPlacementTool(product, 1, customerId);
        var orderId = orderResult.Data!.GetType().GetProperty("orderId")!.GetValue(orderResult.Data) as string;
        var r = CustomerSupportTools.ComplaintRegistrationTool(
            orderId!, $"şikayet metni unique {Guid.NewGuid()}", null);
        r.Success.Should().BeTrue();
        var inferred = (bool)r.Data!.GetType().GetProperty("customerIdInferred")!.GetValue(r.Data)!;
        inferred.Should().BeTrue();
    }

    // ─── GetLastOrderTool ───
    [Fact]
    public void GetLastOrder_BlankCustomer_ValidationError()
    {
        var r = CustomerSupportTools.GetLastOrderTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetLastOrder_NoOrders_NotFound()
    {
        var r = CustomerSupportTools.GetLastOrderTool($"CUST-NEW-{Guid.NewGuid():N}");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetLastOrder_KnownCustomer_Ok()
    {
        var r = CustomerSupportTools.GetLastOrderTool("CUST-1990");
        r.Success.Should().BeTrue();
    }

    // ─── GetAllOrdersTool ───
    [Fact]
    public void GetAllOrders_BlankCustomer_ValidationError()
    {
        var r = CustomerSupportTools.GetAllOrdersTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetAllOrders_NoOrders_NotFound()
    {
        var r = CustomerSupportTools.GetAllOrdersTool($"CUST-EMPTY-{Guid.NewGuid():N}");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetAllOrders_KnownCustomer_Ok()
    {
        var r = CustomerSupportTools.GetAllOrdersTool("CUST-1990");
        r.Success.Should().BeTrue();
        var total = (int)r.Data!.GetType().GetProperty("totalCount")!.GetValue(r.Data)!;
        total.Should().BeGreaterThan(0);
    }

    // ─── HumanHandoffTool ───
    [Fact]
    public void HumanHandoff_BlankReason_ValidationError()
    {
        var r = CustomerSupportTools.HumanHandoffTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void HumanHandoff_WithReason_Ok()
    {
        var r = CustomerSupportTools.HumanHandoffTool("kullanıcı açıkça istedi");
        r.Success.Should().BeTrue();
        var requested = (bool)r.Data!.GetType().GetProperty("handoffRequested")!.GetValue(r.Data)!;
        requested.Should().BeTrue();
    }
}
