using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Application.Services.UiHint;

namespace CustomerSupportBot.Api.Tests.Helpers;

public static class TestFactory
{
    public static CustomerSupportToolsService CreateToolsService(
        IProductCatalogRepository products,
        IOrderRepository orders,
        IComplaintRepository complaints,
        ICustomerRepository? customers = null,
        IUiHintEmitter? uiHint = null,
        SideEffectIdempotencyCache? idempotency = null)
    {
        // Order ve Complaint servisleri üretimde AYNI cache örneğini paylaşır.
        var cache = idempotency ?? new SideEffectIdempotencyCache();

        var productSvc   = new ProductToolsService(products, uiHint ?? new UiHintEmitter(new ApprovalContextAccessor()));
        var orderSvc     = new OrderToolsService(orders, products, customers ?? PermissiveCustomerRepository(), cache);
        var complaintSvc = new ComplaintToolsService(complaints, orders, cache);
        return new CustomerSupportToolsService(productSvc, orderSvc, complaintSvc);
    }

    /// <summary>
    /// OrderToolsService sipariş oluştururken <c>ICustomerRepository.Exists</c> ile müşteriyi
    /// doğruluyor. Çıplak <c>Substitute.For&lt;ICustomerRepository&gt;()</c> her zaman
    /// <c>false</c> döndüğü için müşteri doğrulamasını hedeflemeyen testler de
    /// CUSTOMER_NOT_FOUND ile düşüyordu. Varsayılan stub artık her müşteriyi var kabul eder;
    /// doğrulama davranışını test etmek isteyen çağıran kendi repo'sunu geçebilir.
    /// </summary>
    private static ICustomerRepository PermissiveCustomerRepository()
    {
        var repo = Substitute.For<ICustomerRepository>();
        repo.Exists(Arg.Any<long>()).Returns(true);
        return repo;
    }
}
