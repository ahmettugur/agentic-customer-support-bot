using CustomerSupportBot.Application.Services.Tools;

namespace CustomerSupportBot.Api.Tests.Helpers;

public static class TestFactory
{
    public static CustomerSupportToolsService CreateToolsService(
        IProductCatalogRepository products,
        IOrderRepository orders,
        IComplaintRepository complaints)
    {
        var productSvc   = new ProductToolsService(products);
        var orderSvc     = new OrderToolsService(orders, products);
        var complaintSvc = new ComplaintToolsService(complaints, orders);
        return new CustomerSupportToolsService(productSvc, orderSvc, complaintSvc);
    }
}
