using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Tools;

[Collection("PostgresCatalog")]
public class CustomerSupportToolsTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly CustomerSupportToolsService _svc;

    public CustomerSupportToolsTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
        _svc = TestFactory.CreateToolsService(fixture.ProductRepo, fixture.OrderRepo, fixture.ComplaintRepo);
    }

    // ═══ ProductInquiryTool / ProductListTool ═══
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
        var firstProduct = _fixture.ProductRepo.GetAll().First().Name;
        var r = _svc.ProductInquiryTool(firstProduct);
        r.Success.Should().BeTrue();
        r.Confidence.Should().Be(1.0);
        r.Message.Should().Contain(firstProduct);
    }

    // ═══ OrderPlacementTool ═══
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
        var r = _svc.OrderPlacementTool("ZZZ-yokboyle", 1, "9001");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
    }

    [Fact]
    public void OrderPlacement_Valid_CreatesOrder()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var customerId = "9002";
        var r = _svc.OrderPlacementTool(product, 1, customerId);
        r.Success.Should().BeTrue();
        _fixture.OrderRepo.GetByCustomer(customerId).Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OrderPlacement_DuplicateWithinWindow_ReturnsCachedResult()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var customerId = "9003";
        var ordersBefore = _fixture.OrderRepo.GetByCustomer(customerId).Count;

        var r1 = _svc.OrderPlacementTool(product, 1, customerId);
        var r2 = _svc.OrderPlacementTool(product, 1, customerId);

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();

        // İkinci çağrı YENİ sipariş oluşturmamalı...
        _fixture.OrderRepo.GetByCustomer(customerId).Count
            .Should().Be(ordersBefore + 1, "mükerrer çağrı ikinci bir sipariş yazmamalı");

        // ...ama sonucu sessizce taklit etmek yerine mükerrer olduğunu bildirmeli.
        r2.Message.Should().NotBe(r1.Message);
        r2.Message.Should().Contain("az önce");
        r2.Data.Should().NotBeNull();
        r2.Data!.GetType().GetProperty("duplicate")!.GetValue(r2.Data).Should().Be(true);

        // Uyarı, ilk siparişin numarasını taşımalı.
        var firstOrderId = r1.Data!.GetType().GetProperty("orderId")!.GetValue(r1.Data)!.ToString();
        r2.Message.Should().Contain(firstOrderId);
    }

    [Fact]
    public void OrderPlacement_OutsideWindow_CreatesSecondOrder()
    {
        // Pencere dışına çıkıldığında mükerrer koruması devreye girmemeli.
        var window = TimeSpan.FromSeconds(60);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new SideEffectIdempotencyCache(window, clock: clock);
        var svc = TestFactory.CreateToolsService(
            _fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo, idempotency: cache);

        var product = _fixture.ProductRepo.GetAll().First().Name;
        var customerId = "9005";

        var r1 = svc.OrderPlacementTool(product, 1, customerId);
        clock.Advance(window + TimeSpan.FromSeconds(1));
        var r2 = svc.OrderPlacementTool(product, 1, customerId);

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        _fixture.OrderRepo.GetByCustomer(customerId).Count.Should().Be(2);
    }

    [Fact]
    public void OrderPlacement_StockInsufficient_Conflict()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var r = _svc.OrderPlacementTool(product, 999_999, "9004");
        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.StockInsufficient);
    }

    // ═══ OrderStatusTool ═══
    [Fact]
    public void OrderStatus_BlankId_ValidationError()
    {
        var r = _svc.OrderStatusTool("", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderStatus_KnownOrder_Ok()
    {
        var r = _svc.OrderStatusTool("1030", "1027");
        r.Success.Should().BeTrue();
        r.Message.Should().Contain("1030");
    }

    [Fact]
    public void OrderStatus_UnknownOrder_NotFound()
    {
        var r = _svc.OrderStatusTool("9999", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    [Fact]
    public void OrderStatus_BelongsToDifferentCustomer_IsIndistinguishableFromNotFound()
    {
        // Enumeration oracle koruması: "sipariş yok" ile "var ama başkasının" KULLANICIYA
        // aynı görünmeli. Hata KODU farklıdır (trace/admin görünürlüğü için) ama kullanıcıya
        // giden message birebir aynı olmalı — aksi halde ardışık sipariş numaraları taranarak
        // hangi ID'lerin var olduğu çıkarılabilir.
        var mismatch = _svc.OrderStatusTool("1030", "9999");   // var, ama 1027'nin
        var missing  = _svc.OrderStatusTool("9999", "9999");   // hiç yok

        mismatch.Success.Should().BeFalse();
        mismatch.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch,
            "gerçek sebep operasyonel görünürlük için korunmalı");
        missing.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);

        // Mesajlar yalnızca sorulan sipariş numarasında farklılaşmalı; şablon birebir aynı.
        Normalize(mismatch.Message, "1030").Should().Be(Normalize(missing.Message, "9999"),
            "kullanıcıya giden metin iki durumda da aynı olmalı (enumeration oracle'ı olmasın)");

        static string Normalize(string message, string orderId) => message.Replace(orderId, "<id>");
    }

    // ═══ ComplaintRegistrationTool ═══
    [Fact]
    public void Complaint_MissingFields_ValidationError()
    {
        var r = _svc.ComplaintRegistrationTool("", "kısa", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_TooShortDescription_ValidationError()
    {
        var r = _svc.ComplaintRegistrationTool("1030", "az", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_UnknownOrder_NotFound()
    {
        var r = _svc.ComplaintRegistrationTool(
            "9999", "şikayet açıklaması burada yer alır", "9005");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    [Fact]
    public void Complaint_CustomerIdMismatch_Conflict()
    {
        var r = _svc.ComplaintRegistrationTool(
            "1030", "Urun hatali geldi paket acilmis", "9999");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);
    }

    [Fact]
    public void Complaint_OmitCustomerId_Inferred()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var customerId = "9006";
        var orderResult = _svc.OrderPlacementTool(product, 1, customerId);
        var orderId = orderResult.Data!.GetType().GetProperty("orderId")!.GetValue(orderResult.Data) as string;
        var r = _svc.ComplaintRegistrationTool(
            orderId!, $"şikayet metni unique {Guid.NewGuid()}", null);
        r.Success.Should().BeTrue();
        var inferred = (bool)r.Data!.GetType().GetProperty("customerIdInferred")!.GetValue(r.Data)!;
        inferred.Should().BeTrue();
    }

    // ═══ OrderCancelTool ═══
    [Fact]
    public void OrderCancel_BlankOrderId_ValidationError()
    {
        var r = _svc.OrderCancelTool("", "geçerli bir iptal sebebi", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderCancel_ShortReason_ValidationError()
    {
        // Sınır: 4 karakter → reddedilmeli (min 5).
        var r = _svc.OrderCancelTool("1030", "1234", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
        r.Error.MissingFields.Should().Contain(WellKnown.ToolParameterNames.Reason);
    }

    [Fact]
    public void OrderCancel_WhitespacePaddedReason_ValidationError()
    {
        // Ham uzunluk 5 ama trim sonrası 1 — boşluk dolgusu kuralı aşmamalı.
        var r = _svc.OrderCancelTool("1030", "a    ", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void OrderCancel_ExactlyFiveCharReason_CancelsOrder()
    {
        // Sınır: tam 5 karakter geçerli → doğrulama geçip iptal akışı sürmeli.
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var customerId = "9501";
        var placed = _svc.OrderPlacementTool(product, 1, customerId);
        var orderId = placed.Data!.GetType().GetProperty("orderId")!.GetValue(placed.Data) as string;

        var r = _svc.OrderCancelTool(orderId!, "12345", customerId);
        r.Success.Should().BeTrue();
        _fixture.OrderRepo.Get(orderId!)!.Status.Should().Be(WellKnown.OrderStatuses.Cancelled);
    }

    [Fact]
    public void OrderCancel_BelongsToDifferentCustomer_Conflict()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var placed = _svc.OrderPlacementTool(product, 1, "9503");
        var orderId = placed.Data!.GetType().GetProperty("orderId")!.GetValue(placed.Data) as string;

        var r = _svc.OrderCancelTool(orderId!, "12345", "9999");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);
        _fixture.OrderRepo.Get(orderId!)!.Status.Should().NotBe(WellKnown.OrderStatuses.Cancelled);

        // Kullanıcıya giden metin "hiç yok" durumuyla aynı olmalı (enumeration oracle'ı yok);
        // sadece sorulan sipariş numarası kısmı farklılaşabilir.
        var missing = _svc.OrderCancelTool("9999", "12345", "9999");
        r.Message.Replace(orderId!, "<id>").Should().Be(missing.Message.Replace("9999", "<id>"));
    }

    // ═══ ReturnRequestTool ═══
    [Fact]
    public void ReturnRequest_BlankOrderId_ValidationError()
    {
        var r = _svc.ReturnRequestTool("", "geçerli bir iade sebebi", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void ReturnRequest_ShortReason_ValidationError()
    {
        // Sınır: 4 karakter → reddedilmeli (min 5).
        var r = _svc.ReturnRequestTool("1030", "1234", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
        r.Error.MissingFields.Should().Contain(WellKnown.ToolParameterNames.Reason);
    }

    [Fact]
    public void ReturnRequest_ExactlyFiveCharReason_PassesValidation()
    {
        // Bilinmeyen sipariş + geçerli sebep: doğrulama geçip akış NotFound'a ilerlemeli.
        var r = _svc.ReturnRequestTool("9999", "12345", "1027");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    [Fact]
    public void ReturnRequest_BelongsToDifferentCustomer_Conflict()
    {
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var orderId = _fixture.OrderRepo.Create(new OrderInfo
        {
            Product = product,
            Quantity = 1,
            CustomerId = "9504",
            Status = WellKnown.OrderStatuses.Delivered,
            OrderDate = DateTime.UtcNow
        });

        var r = _svc.ReturnRequestTool(orderId, "ürün hasarlı geldi", "9999");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);

        // Kullanıcıya giden metin "hiç yok" durumuyla aynı olmalı (enumeration oracle'ı yok);
        // sadece sorulan sipariş numarası kısmı farklılaşabilir.
        var missing = _svc.ReturnRequestTool("9999", "ürün hasarlı geldi", "9999");
        r.Message.Replace(orderId, "<id>").Should().Be(missing.Message.Replace("9999", "<id>"));
    }

    [Fact]
    public void ReturnRequest_ValidInput_CreatesReturnRequest()
    {
        // Seed siparişleri 2006 tarihli (14 gün penceresi dolmuş) — taze bir
        // 'Teslim Edildi' siparişi doğrudan repo üzerinden oluşturuluyor.
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var orderId = _fixture.OrderRepo.Create(new OrderInfo
        {
            Product = product,
            Quantity = 1,
            CustomerId = "9502",
            Status = WellKnown.OrderStatuses.Delivered,
            OrderDate = DateTime.UtcNow
        });

        var r = _svc.ReturnRequestTool(orderId, "ürün hasarlı geldi", "9502");
        r.Success.Should().BeTrue();
        _fixture.OrderRepo.Get(orderId)!.Status.Should().Be(WellKnown.OrderStatuses.ReturnRequested);
    }

    // ═══ ComplaintRegistrationTool — sınır değerleri ═══
    [Fact]
    public void Complaint_NineCharDescription_ValidationError()
    {
        // Sınır: 9 karakter → reddedilmeli (min 10).
        var r = _svc.ComplaintRegistrationTool("1030", "123456789", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_WhitespacePaddedDescription_ValidationError()
    {
        // Ham uzunluk 10 ama trim sonrası 2 — boşluk dolgusu kuralı aşmamalı.
        var r = _svc.ComplaintRegistrationTool("1030", "ab        ", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Complaint_ExactlyTenCharDescription_PassesValidation()
    {
        // Bilinmeyen sipariş + geçerli metin: doğrulama geçip akış NotFound'a ilerlemeli.
        var r = _svc.ComplaintRegistrationTool("9999", "1234567890", null);
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotFound);
    }

    // ═══ GetLastOrderTool ═══
    [Fact]
    public void GetLastOrder_BlankCustomer_ValidationError()
    {
        var r = _svc.GetLastOrderTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetLastOrder_NoOrders_NotFound()
    {
        var r = _svc.GetLastOrderTool("8888");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetLastOrder_KnownCustomer_Ok()
    {
        var r = _svc.GetLastOrderTool("1008");
        r.Success.Should().BeTrue();
    }

    // ═══ GetAllOrdersTool ═══
    [Fact]
    public void GetAllOrders_BlankCustomer_ValidationError()
    {
        var r = _svc.GetAllOrdersTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void GetAllOrders_NoOrders_NotFound()
    {
        var r = _svc.GetAllOrdersTool("7777");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoOrdersForCustomer);
    }

    [Fact]
    public void GetAllOrders_KnownCustomer_Ok()
    {
        var r = _svc.GetAllOrdersTool("1008");
        r.Success.Should().BeTrue();
        var total = (int)r.Data!.GetType().GetProperty("totalCount")!.GetValue(r.Data)!;
        total.Should().BeGreaterThan(0);
    }

    // ═══ HumanHandoffTool ═══
    [Fact]
    public void HumanHandoff_BlankReason_ValidationError()
    {
        var r = CustomerSupportToolsService.HumanHandoffTool("");
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void HumanHandoff_WithReason_Ok()
    {
        var r = CustomerSupportToolsService.HumanHandoffTool("kullanıcı açıkça istedi");
        r.Success.Should().BeTrue();
        var requested = (bool)r.Data!.GetType().GetProperty("handoffRequested")!.GetValue(r.Data)!;
        requested.Should().BeTrue();
    }
}
