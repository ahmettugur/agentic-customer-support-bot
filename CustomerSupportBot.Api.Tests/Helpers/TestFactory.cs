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
        IUiHintEmitter? uiHint = null)
    {
        var productSvc   = new ProductToolsService(products, uiHint ?? new UiHintEmitter(new ApprovalContextAccessor()));
        var orderSvc     = new OrderToolsService(orders, products, customers ?? Substitute.For<ICustomerRepository>());
        var complaintSvc = new ComplaintToolsService(complaints, orders);
        return new CustomerSupportToolsService(productSvc, orderSvc, complaintSvc);
    }
}
