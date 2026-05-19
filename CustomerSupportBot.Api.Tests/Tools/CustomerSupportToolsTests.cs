using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Tools;

/// <summary>
/// CustomerSupportToolsService testleri � InMemory adapter'larla izole edilmi�.
/// </summary>
public class CustomerSupportToolsTests
{
    private readonly InMemoryProductCatalogAdapter _products = new();
    private readonly InMemoryOrderAdapter _orders = new();
    private readonly InMemoryComplaintAdapter _complaints = new();
    private readonly CustomerSupportToolsService _svc;

    public CustomerSupportToolsTests()
    {
        _svc = new CustomerSupportToolsService(_products, _orders, _complaints);
    }

    // ��� ProductInquiryTool ���
    [Fact]
    public void ProductInquiry_BlankName_ValidationError()
    {
        var r = _svc.ProductInquiryTool("");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void ProductInquiry_UnknownProduct_NotFound()
    {
        var r = _svc.ProductInquiryTool("ZZZ-yokboyle-urun-xyz");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
    }

    [Fact]
    public void ProductInquiry_KnownProduct_Ok()
    {
        var firstProduct = _products.GetAll().First().Name;
        var r = _svc.ProductInquiryTool(firstProduct);
        r.Success.Should().BeTrue();
        r.Confidence.Should().Be(1.0);
        r.Message.Should().Contain(firstProduct);
    }

    // ��� OrderPlacementTool ���
    [Fact]
    public void OrderPlacement_AllMissing_ValidationError()
    {
        var r = _svc.OrderPlacementTool("", null, "");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderPlacement_UnknownProduct_NotFound()
    {
        var r = _svc.OrderPlacementTool("ZZZ-yokboyle", 1, "CUST-1990");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
    }

    [Fact]
    public void OrderPlacement_Valid_CreatesOrder()
    {
        var product = _products.GetAll().First().Name;
        var customerId = $"CUST-TEST-{Guid.NewGuid():N}";
        var r = _svc.OrderPlacementTool(product, 1, customerId);
        r.Success.Should().BeTrue();
        _orders.GetByCustomer(customerId).Count.Should().Be(1);
    }

    [Fact]
    public void OrderPlacement_DuplicateWithinWindow_ReturnsCachedResult()
    {
        var product = _products.GetAll().First().Name;
        var customerId = $"CUST-IDEM-{Guid.NewGuid():N}";
        var r1 = _svc.OrderPlacementTool(product, 1, customerId);
        var r2 = _svc.OrderPlacementTool(product, 1, customerId);
        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        r1.Message.Should().Be(r2.Message);
    }

    [Fact]
    public void OrderPlacement_StockInsufficient_Conflict()
    {
        var product = _products.GetAll().First().Name;
        var r = _svc.OrderPlacementTool(product, 999_999, $"CUST-X-{Guid.NewGuid():N}");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.StockInsufficient);
    }

    // ��� OrderStatusTool ���
    [Fact]
    public void OrderStatus_BlankId_ValidationError()
    {
        var r = _svc.OrderStatusTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderStatus_KnownOrder_Ok()
    {
        var r = _svc.OrderStatusTool("ORD-1");
        r.Success.Should().BeTrue();
        r.Message.Should().Contain("ORD-1");
    }

    [Fact]
    public void OrderStatus_UnknownOrder_NotFound()
    {
        var r = _svc.OrderStatusTool("ORD-9999999");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    // ��� ComplaintRegistrationTool ���
    [Fact]
    public void Complaint_MissingFields_ValidationError()
    {
        var r = _svc.ComplaintRegistrationTool("", "k�sa", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_TooShortDescription_ValidationError()
    {
        var r = _svc.ComplaintRegistrationTool("ORD-1", "az", "CUST-1990");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_UnknownOrder_NotFound()
    {
        var r = _svc.ComplaintRegistrationTool(
            "ORD-99999", "�ikayet a��klamas� burada yer al�r", "CUST-X");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    [Fact]
    public void Complaint_CustomerIdMismatch_Conflict()
    {
        var r = _svc.ComplaintRegistrationTool(
            "ORD-1", "�r�n hatal� geldi paket a��lm��", "CUST-WRONG-XYZ");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);
    }

    [Fact]
    public void Complaint_OmitCustomerId_Inferred()
    {
        var product = _products.GetAll().First().Name;
        var customerId = $"CUST-COMPL-{Guid.NewGuid():N}";
        var orderResult = _svc.OrderPlacementTool(product, 1, customerId);
        var orderId = orderResult.Data!.GetType().GetProperty("orderId")!.GetValue(orderResult.Data) as string;
        var r = _svc.ComplaintRegistrationTool(
            orderId!, $"�ikayet metni unique {Guid.NewGuid()}", null);
        r.Success.Should().BeTrue();
        var inferred = (bool)r.Data!.GetType().GetProperty("customerIdInferred")!.GetValue(r.Data)!;
        inferred.Should().BeTrue();
    }

    // ��� GetLastOrderTool ���
    [Fact]
    public void GetLastOrder_BlankCustomer_ValidationError()
    {
        var r = _svc.GetLastOrderTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetLastOrder_NoOrders_NotFound()
    {
        var r = _svc.GetLastOrderTool($"CUST-NEW-{Guid.NewGuid():N}");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetLastOrder_KnownCustomer_Ok()
    {
        var r = _svc.GetLastOrderTool("CUST-1990");
        r.Success.Should().BeTrue();
    }

    // ��� GetAllOrdersTool ���
    [Fact]
    public void GetAllOrders_BlankCustomer_ValidationError()
    {
        var r = _svc.GetAllOrdersTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetAllOrders_NoOrders_NotFound()
    {
        var r = _svc.GetAllOrdersTool($"CUST-EMPTY-{Guid.NewGuid():N}");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetAllOrders_KnownCustomer_Ok()
    {
        var r = _svc.GetAllOrdersTool("CUST-1990");
        r.Success.Should().BeTrue();
        var total = (int)r.Data!.GetType().GetProperty("totalCount")!.GetValue(r.Data)!;
        total.Should().BeGreaterThan(0);
    }

    // ��� HumanHandoffTool ���
    [Fact]
    public void HumanHandoff_BlankReason_ValidationError()
    {
        var r = CustomerSupportToolsService.HumanHandoffTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void HumanHandoff_WithReason_Ok()
    {
        var r = CustomerSupportToolsService.HumanHandoffTool("kullan�c� a��k�a istedi");
        r.Success.Should().BeTrue();
        var requested = (bool)r.Data!.GetType().GetProperty("handoffRequested")!.GetValue(r.Data)!;
        requested.Should().BeTrue();
    }
}
