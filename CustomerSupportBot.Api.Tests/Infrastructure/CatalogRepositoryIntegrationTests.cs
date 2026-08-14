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
        order!.Lines.Should().ContainSingle()
            .Which.Product.Should().Be("Bira");   // seed Türkçeleştirildi (eski: "Beer")
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
            Lines      = [new OrderLine("Kahve", 5)],
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
    public void Create_MultiLineOrder_PersistsEveryLine()
    {
        // Şema (catalog.order_details) baştan beri çok satırlıydı; eskiden model tek
        // satıra düşürdüğü için ikinci ve sonraki ürünler sessizce kayboluyordu.
        var order = new OrderInfo
        {
            Lines      = [new OrderLine("Kahve", 2), new OrderLine("Çikolata", 1)],
            CustomerId = "9001",
            Status     = WellKnown.OrderStatuses.Processing,
            OrderDate  = DateTime.UtcNow
        };

        var code = _fx.OrderRepo.Create(order);
        var reloaded = _fx.OrderRepo.Get(code);

        reloaded.Should().NotBeNull();
        reloaded!.Lines.Should().HaveCount(2);
        reloaded.TotalQuantity().Should().Be(3);
        reloaded.Lines.Select(l => l.Product).Should().BeEquivalentTo(["Kahve", "Çikolata"]);
    }

    [Fact]
    public void Create_OrderWithNoLines_Throws()
    {
        var order = new OrderInfo
        {
            CustomerId = "9001",
            Status     = WellKnown.OrderStatuses.Processing,
            OrderDate  = DateTime.UtcNow
        };

        // Satırsız "hayalet sipariş" yazılmamalı.
        var act = () => _fx.OrderRepo.Create(order);
        act.Should().Throw<ArgumentException>();
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

    // NOT: NorthwindSeedData Türkçeleştirildi (Coffee→Kahve, Chocolate→Çikolata).
    // Bu testler İngilizce adlarla kalmıştı; fixture hiç ayağa kalkmadığı için
    // uyumsuzluk fark edilmemişti.
    [Fact]
    public void FindProduct_ExactName_ReturnsProduct()
    {
        var p = _fx.ProductRepo.FindProduct("Kahve");
        p.Should().NotBeNull();
        p!.Price.Should().Be(46.00m);
    }

    [Fact]
    public void FindProduct_CaseAndAccentInsensitive_ReturnsProduct()
    {
        // "und-u-ks-level1" ICU collation'ı non-deterministic: büyük/küçük harf ve
        // aksan duyarsız eşleşme sağlar. Bu test collation'ın gerçekten uygulandığını doğrular.
        var p = _fx.ProductRepo.FindProduct("çikolata");
        p.Should().NotBeNull();
        p!.Name.Should().Be("Çikolata");
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
        var result = _fx.ProductRepo.TryDeductStock([new OrderLine("Scones", 1)]);
        result.Success.Should().BeTrue();
        var after = _fx.ProductRepo.FindProduct("Scones")!.Stock;
        after.Should().Be(before - 1);
    }

    [Fact]
    public void TryDeductStock_InsufficientStock_ReturnsFailureWithShortage()
    {
        var available = _fx.ProductRepo.FindProduct("Çikolata")!.Stock;

        var result = _fx.ProductRepo.TryDeductStock([new OrderLine("Çikolata", 99_999)]);

        result.Success.Should().BeFalse();
        result.Shortages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new StockShortage("Çikolata", 99_999, available));
    }

    [Fact]
    public void TryDeductStock_MultiLine_AllOrNothing()
    {
        // Bir satır yetmezse diğerlerinin stoğu da düşülmemeli — aksi hâlde sipariş
        // oluşmadığı hâlde stok sessizce kaybolurdu.
        var sconesBefore = _fx.ProductRepo.FindProduct("Scones")!.Stock;

        var result = _fx.ProductRepo.TryDeductStock(
            [new OrderLine("Scones", 1), new OrderLine("Çikolata", 99_999)]);

        result.Success.Should().BeFalse();
        result.Shortages.Should().ContainSingle().Which.Product.Should().Be("Çikolata");
        _fx.ProductRepo.FindProduct("Scones")!.Stock.Should().Be(sconesBefore);
    }

    [Fact]
    public void TryDeductStock_EmptyLines_Succeeds()
    {
        _fx.ProductRepo.TryDeductStock([]).Success.Should().BeTrue();
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
