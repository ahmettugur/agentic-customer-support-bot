// Helpers/TestFactory.cs — Test projesinde sık kullanılan nesnelerin factory metodları.

using CustomerSupportBot.Application.Ports.Driven.Persistence;

namespace CustomerSupportBot.Api.Tests.Helpers;

/// <summary>
/// Test bağımlılıklarını oluşturmak için merkezi factory.
/// InMemory adapter'larla entegrasyon testlerini kolaylaştırır.
/// </summary>
public static class TestFactory
{
    public static InMemoryProductCatalogAdapter CreateProducts() => new();
    public static InMemoryOrderAdapter CreateOrders() => new();
    public static InMemoryComplaintAdapter CreateComplaints() => new();

    public static CustomerSupportToolsService CreateToolsService(
        IProductCatalogRepository? products = null,
        IOrderRepository? orders = null,
        IComplaintRepository? complaints = null)
        => new(products ?? CreateProducts(), orders ?? CreateOrders(), complaints ?? CreateComplaints());
}
