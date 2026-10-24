// Proaktif geciken-kargo taramasının sorgusu: yalnızca hâlâ "Kargolandı" olan ve eşikten ÖNCE kargolananlar, en eskiden yeniye.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresShippedBeforeTests(PostgresCatalogFixture fixture)
{
    private async Task<string> OrderAsync(string status, int? shippedDaysAgo)
    {
        await using var ctx = fixture.DbFactory.CreateDbContext();
        var ct = TestContext.Current.CancellationToken;
        var productId = ctx.Products.Select(p => p.Id).First();
        var order = new OrderEntity
        {
            CustomerId = 1001, Status = status, OrderDate = DateTime.UtcNow.AddDays(-30),
            ShippedAt = shippedDaysAgo is { } d ? DateTime.UtcNow.AddDays(-d) : null
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        ctx.OrderDetails.Add(new OrderDetailEntity { OrderCode = order.Code, ProductId = productId, Quantity = 1 });
        await ctx.SaveChangesAsync(ct);
        return order.Code.ToString();
    }

    [Fact]
    public async Task ReturnsOnlyStillShippedOrders_ShippedBeforeTheCutoff_OldestFirst()
    {
        var old = await OrderAsync(WellKnown.OrderStatuses.Shipped, shippedDaysAgo: 9);
        var older = await OrderAsync(WellKnown.OrderStatuses.Shipped, shippedDaysAgo: 12);
        var fresh = await OrderAsync(WellKnown.OrderStatuses.Shipped, shippedDaysAgo: 1);
        var delivered = await OrderAsync(WellKnown.OrderStatuses.Delivered, shippedDaysAgo: 10);
        var noDate = await OrderAsync(WellKnown.OrderStatuses.Shipped, shippedDaysAgo: null);

        var ids = fixture.OrderRepo.GetShippedBefore(DateTime.UtcNow.AddDays(-5), 100).Select(x => x.OrderId).ToList();

        ids.Should().Contain([older, old]);
        ids.IndexOf(older).Should().BeLessThan(ids.IndexOf(old));
        ids.Should().NotContain([fresh, delivered, noDate]);
    }

    [Fact]
    public async Task TheLimit_IsHonoured()
    {
        await OrderAsync(WellKnown.OrderStatuses.Shipped, 8);
        await OrderAsync(WellKnown.OrderStatuses.Shipped, 9);

        fixture.OrderRepo.GetShippedBefore(DateTime.UtcNow.AddDays(-5), 1).Should().HaveCount(1);
    }
}
