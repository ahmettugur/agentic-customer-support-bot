// Sipariş durum geçişleri (fulfillment): kargolandı → teslim edildi, atomik koşullu güncelleme.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresOrderShippingTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private async Task<string> OrderAsync(string status)
    {
        await using var ctx = fixture.DbFactory.CreateDbContext();
        var productId = await ctx.Products.Select(p => p.Id).FirstAsync(Ct);
        var order = new OrderEntity { CustomerId = 1001, Status = status, OrderDate = T0.AddDays(-1) };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(Ct);
        ctx.OrderDetails.Add(new OrderDetailEntity { OrderCode = order.Code, ProductId = productId, Quantity = 1 });
        await ctx.SaveChangesAsync(Ct);
        return order.Code.ToString();
    }

    [Fact]
    public async Task Processing_Shipped_Delivered()
    {
        var id = await OrderAsync(WellKnown.OrderStatuses.Processing);

        var shipped = fixture.OrderRepo.MarkShipped(id, "Yurtiçi Kargo", "YK123456", T0);
        shipped.Change.Should().Be(OrderStatusChange.Updated);
        shipped.Order!.Status.Should().Be(WellKnown.OrderStatuses.Shipped);

        var reloaded = fixture.OrderRepo.Get(id)!;
        reloaded.Carrier.Should().Be("Yurtiçi Kargo");
        reloaded.TrackingNumber.Should().Be("YK123456");
        reloaded.ShippedAt.Should().Be(T0);

        var delivered = fixture.OrderRepo.MarkDelivered(id, T0.AddDays(2));
        delivered.Change.Should().Be(OrderStatusChange.Updated);
        fixture.OrderRepo.Get(id)!.DeliveredAt.Should().Be(T0.AddDays(2), "iade süresi teslimden sayılır");
        fixture.OrderRepo.Get(id)!.TrackingNumber.Should().Be("YK123456", "teslim kargo bilgisini silmez");
    }

    [Fact]
    public async Task SameStatusAgain_IsUnchanged_AndKeepsTheFirstData()
    {
        var id = await OrderAsync(WellKnown.OrderStatuses.Processing);
        fixture.OrderRepo.MarkShipped(id, "Aras", "A1", T0);

        var again = fixture.OrderRepo.MarkShipped(id, "MNG", "M2", T0.AddHours(1));

        again.Change.Should().Be(OrderStatusChange.Unchanged);
        fixture.OrderRepo.Get(id)!.TrackingNumber.Should().Be("A1");
    }

    [Theory]
    [InlineData("İptal Edildi", true)]       // iptal edilmiş sipariş kargolanamaz
    [InlineData("Teslim Edildi", true)]      // teslim edilmiş geri dönmez
    [InlineData("İşleniyor", false)]         // kargolanmadan teslim edilemez
    public async Task InvalidTransitions(string status, bool ship)
    {
        var id = await OrderAsync(status);

        var r = ship ? fixture.OrderRepo.MarkShipped(id, null, null, T0) : fixture.OrderRepo.MarkDelivered(id, T0);

        r.Change.Should().Be(OrderStatusChange.InvalidTransition);
        r.Order!.Status.Should().Be(status);
    }

    [Theory]
    [InlineData("999999999")]
    [InlineData("abc")]
    public void UnknownOrder_IsNotFound(string id)
    {
        fixture.OrderRepo.MarkShipped(id, null, null, T0).Change.Should().Be(OrderStatusChange.NotFound);
        fixture.OrderRepo.MarkDelivered(id, T0).Change.Should().Be(OrderStatusChange.NotFound);
    }
}
