using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Infrastructure;

[Collection("PostgresCatalog")]
public sealed class OrderRepositoryIntegrationTests
{
    private readonly PostgresCatalogFixture _fx;

    public OrderRepositoryIntegrationTests(PostgresCatalogFixture fx) => _fx = fx;

    [Fact]
    public void Get_ExistingOrder_ReturnsOrder()
    {
        var order = _fx.OrderRepo.Get("1030");
        order.Should().NotBeNull();
        order!.Product.Should().Be("Beer");
        order.CustomerId.Should().Be("1027");
    }

    [Fact]
    public void Get_UnknownOrder_ReturnsNull()
    {
        _fx.OrderRepo.Get("99999").Should().BeNull();
    }

    [Fact]
    public void GetByCustomer_KnownCustomer_ReturnsSortedByDate()
    {
        // Customer 1008 has: 1033 (Jan), 1039 (Mar), 1048 (Apr), 1060 (Apr), 1075 (Jun)
        var orders = _fx.OrderRepo.GetByCustomer("1008");
        orders.Should().HaveCountGreaterThanOrEqualTo(2);
        orders[0].Order.OrderDate.Should().BeOnOrAfter(orders[1].Order.OrderDate);
    }

    [Fact]
    public void GetLast_KnownCustomer_ReturnsLatest()
    {
        var last = _fx.OrderRepo.GetLast("1008");
        last.Should().NotBeNull();
    }

    [Fact]
    public void Create_ValidOrder_AssignsNewCode()
    {
        var order = new OrderInfo
        {
            Product    = "Coffee",
            Quantity   = 5,
            CustomerId = "9001",
            Status     = WellKnown.OrderStatuses.Processing,
            OrderDate  = DateTime.UtcNow
        };

        var code = _fx.OrderRepo.Create(order);
        code.Should().NotBeNullOrEmpty();
        long.TryParse(code, out var id).Should().BeTrue();
        id.Should().BeGreaterThanOrEqualTo(1082);
        _fx.OrderRepo.Get(code).Should().NotBeNull();
    }

    [Fact]
    public void Cancel_ShippedOrder_ChangesStatus()
    {
        // Order 1042 is Shipped
        var result = _fx.OrderRepo.Cancel("1042", "Müşteri iptal istedi");
        result.Should().BeTrue();
        var order = _fx.OrderRepo.Get("1042");
        order!.Status.Should().Be(WellKnown.OrderStatuses.Cancelled);
    }
}

[Collection("PostgresCatalog")]
public sealed class ProductCatalogRepositoryIntegrationTests
{
    private readonly PostgresCatalogFixture _fx;

    public ProductCatalogRepositoryIntegrationTests(PostgresCatalogFixture fx) => _fx = fx;

    [Fact]
    public void FindProduct_ExactName_ReturnsProduct()
    {
        var p = _fx.ProductRepo.FindProduct("Coffee");
        p.Should().NotBeNull();
        p!.Price.Should().Be(46.00m);
    }

    [Fact]
    public void FindProduct_PartialName_ReturnsFirstMatch()
    {
        var p = _fx.ProductRepo.FindProduct("Chocolate");
        p.Should().NotBeNull();
        p!.Name.Should().Contain("Chocolate");
    }

    [Fact]
    public void FindProduct_UnknownName_ReturnsNull()
    {
        _fx.ProductRepo.FindProduct("ZZZ-yok-boyle-urun").Should().BeNull();
    }

    [Fact]
    public void TryDeductStock_SufficientStock_DeductsAndReturnsTrue()
    {
        var before = _fx.ProductRepo.FindProduct("Scones")!.Stock;
        var ok = _fx.ProductRepo.TryDeductStock("Scones", 1);
        ok.Should().BeTrue();
        var after = _fx.ProductRepo.FindProduct("Scones")!.Stock;
        after.Should().Be(before - 1);
    }

    [Fact]
    public void TryDeductStock_InsufficientStock_ReturnsFalse()
    {
        _fx.ProductRepo.TryDeductStock("Chai", 99_999).Should().BeFalse();
    }

    [Fact]
    public void GetAll_ReturnsAllProducts()
    {
        _fx.ProductRepo.GetAll().Should().HaveCountGreaterThanOrEqualTo(36);
    }
}

[Collection("PostgresCatalog")]
public sealed class ComplaintRepositoryIntegrationTests
{
    private readonly PostgresCatalogFixture _fx;

    public ComplaintRepositoryIntegrationTests(PostgresCatalogFixture fx) => _fx = fx;

    [Fact]
    public void Get_ExistingComplaint_ReturnsComplaint()
    {
        var c = _fx.ComplaintRepo.Get("1001");
        c.Should().NotBeNull();
        c!.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void Get_UnknownComplaint_ReturnsNull()
    {
        _fx.ComplaintRepo.Get("99999").Should().BeNull();
    }

    [Fact]
    public void GetByOrder_KnownOrder_ReturnsComplaints()
    {
        var list = _fx.ComplaintRepo.GetByOrder("1033");
        list.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Create_ValidComplaint_AssignsNewCode()
    {
        var complaint = new ComplaintInfo
        {
            OrderId    = "1045",
            CustomerId = "1028",
            Complaint  = "Paket hasarlı geldi.",
            Status     = WellKnown.ComplaintStatuses.Pending
        };

        var code = _fx.ComplaintRepo.Create(complaint);
        code.Should().NotBeNullOrEmpty();
        long.TryParse(code, out var id).Should().BeTrue();
        id.Should().BeGreaterThanOrEqualTo(1006);
        _fx.ComplaintRepo.Get(code).Should().NotBeNull();
    }
}
