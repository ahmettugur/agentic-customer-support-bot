// İade süresi NEREDEN sayılıyor?
//
// Politika (KnowledgeBase/iade-politikasi.md) "aldıkları ürünleri 14 gün içinde" diyor — yani
// süre TESLİMDEN başlar. Sipariş tarihinden sayıldığında, 10 günde teslim edilen bir ürünün
// müşterisine fiilen 4 gün kalır; bot da RAG'dan politikayı okuyup müşteriye başka bir şey
// söyler.
//
// Teslim tarihi olmayan eski kayıtlar bugünkü davranışı korur (sipariş tarihi) — bu, bilinen
// en erken ve dolayısıyla en tutucu tarihtir.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresOrderReturnWindowTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresOrderReturnWindowTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>Teslim edilmiş bir sipariş kurar; tarihler "bugünden kaç gün önce" olarak verilir.</summary>
    private async Task<string> DeliveredOrderAsync(int orderedDaysAgo, int? deliveredDaysAgo)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var ct = TestContext.Current.CancellationToken;
        var productId = await ctx.Products.Select(p => p.Id).FirstAsync(ct);
        var now = DateTime.UtcNow;

        var order = new OrderEntity
        {
            CustomerId = 1001,
            Status = WellKnown.OrderStatuses.Delivered,
            OrderDate = now.AddDays(-orderedDaysAgo),
            DeliveredAt = deliveredDaysAgo is { } d ? now.AddDays(-d) : null
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);

        ctx.OrderDetails.Add(new OrderDetailEntity { OrderCode = order.Code, ProductId = productId, Quantity = 1 });
        await ctx.SaveChangesAsync(ct);
        return order.Code.ToString();
    }

    [Fact]
    public async Task LateDelivery_WindowCountsFromDelivery_NotFromOrder()
    {
        // 20 gün önce sipariş, 3 gün önce teslim: sipariş tarihine göre süre dolmuş,
        // teslim tarihine göre 11 gün daha var.
        var orderId = await DeliveredOrderAsync(orderedDaysAgo: 20, deliveredDaysAgo: 3);

        _fixture.OrderRepo.RequestReturn(orderId, "ürün hasarlı geldi")
            .Should().BeTrue("14 gün teslimden itibaren sayılır");
    }

    [Fact]
    public async Task DeliveredMoreThan14DaysAgo_IsRejected()
    {
        var orderId = await DeliveredOrderAsync(orderedDaysAgo: 20, deliveredDaysAgo: 15);

        _fixture.OrderRepo.RequestReturn(orderId, "ürün hasarlı geldi").Should().BeFalse();
    }

    [Fact]
    public async Task LegacyOrderWithoutDeliveryDate_FallsBackToOrderDate()
    {
        var old = await DeliveredOrderAsync(orderedDaysAgo: 20, deliveredDaysAgo: null);
        var recent = await DeliveredOrderAsync(orderedDaysAgo: 5, deliveredDaysAgo: null);

        _fixture.OrderRepo.RequestReturn(old, "ürün hasarlı geldi").Should().BeFalse();
        _fixture.OrderRepo.RequestReturn(recent, "ürün hasarlı geldi").Should().BeTrue();
    }

    [Fact]
    public async Task Get_ExposesTheDeliveryDate()
    {
        var orderId = await DeliveredOrderAsync(orderedDaysAgo: 10, deliveredDaysAgo: 2);

        _fixture.OrderRepo.Get(orderId)!.DeliveredAt
            .Should().BeCloseTo(DateTime.UtcNow.AddDays(-2), TimeSpan.FromMinutes(1));
    }
}
