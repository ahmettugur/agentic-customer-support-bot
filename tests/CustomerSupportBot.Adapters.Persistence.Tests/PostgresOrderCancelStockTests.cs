// Sipariş İPTALİ stoğu geri veriyor mu?
//
// Sipariş verilirken stok düşülür (bkz. PostgresOrderPlacementAtomicityTests). İptal bu
// rezervasyonu serbest bırakmazsa, iptal edilen her siparişin adedi kalıcı olarak kaybolur:
// ürün depoda dururken "stokta yok" görünür ve bunu hiçbir log göstermez.
//
// İade ise tüketir: iptal aynı sipariş için İKİ KEZ stok iade ederse (tekrarlanan onay,
// iki pod'da eşzamanlı yürütme) stok gerçekte olmayan adetlerle şişer. Bu yüzden iade,
// durum geçişiyle aynı atomik adımda ve yalnızca bir kez yapılmalı.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresOrderCancelStockTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresOrderCancelStockTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>Testler birbirinin stoğunu etkilemesin diye her test kendi ürününü yaratır.</summary>
    private async Task<string> NewProductAsync(int stock)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var categoryId = await ctx.Categories.Select(c => c.Id).FirstAsync(TestContext.Current.CancellationToken);
        var name = $"İptal Testi {Guid.NewGuid():N}";
        ctx.Products.Add(new ProductEntity
        {
            Name = name, Stock = stock, Price = 10m, CategoryId = categoryId
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return name;
    }

    private async Task<int> StockOfAsync(string productName)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        return await ctx.Products.Where(p => p.Name == productName)
            .Select(p => p.Stock).FirstAsync(TestContext.Current.CancellationToken);
    }

    private string PlaceOrder(params OrderLine[] lines)
    {
        var result = _fixture.OrderRepo.PlaceOrder(new OrderInfo
        {
            Lines = [.. lines],
            CustomerId = "1001",
            Status = WellKnown.OrderStatuses.Processing,
            OrderDate = DateTime.UtcNow
        });
        result.OrderId.Should().NotBeNullOrEmpty("test kurulumu siparişin oluşmasını gerektirir");
        return result.OrderId!;
    }

    [Fact]
    public async Task Cancel_ReturnsTheStockOfEveryLine()
    {
        var kahve = await NewProductAsync(50);
        var cay = await NewProductAsync(20);
        var orderId = PlaceOrder(new OrderLine(kahve, 5), new OrderLine(cay, 3));

        _fixture.OrderRepo.Cancel(orderId, "vazgeçtim").Should().BeTrue();

        (await StockOfAsync(kahve)).Should().Be(50, "iptal, siparişin düştüğü stoğu geri vermeli");
        (await StockOfAsync(cay)).Should().Be(20);
    }

    [Fact]
    public async Task CancellingTwice_ReturnsTheStockOnlyOnce()
    {
        var kahve = await NewProductAsync(50);
        var orderId = PlaceOrder(new OrderLine(kahve, 5));

        _fixture.OrderRepo.Cancel(orderId, "ilk").Should().BeTrue();
        _fixture.OrderRepo.Cancel(orderId, "ikinci").Should().BeFalse("iptal edilmiş sipariş yeniden iptal edilemez");

        (await StockOfAsync(kahve)).Should().Be(50, "stok bir kez iade edilmeli, iki kez değil");
    }

    /// <summary>
    /// Aynı siparişin iptali eşzamanlı olarak birden çok kez yürütülürse (iki pod, tekrarlanan
    /// onay) yalnızca BİRİ kazanmalı ve stok yalnızca bir kez iade edilmeli. "Oku → durumu
    /// kontrol et → yaz" dizisinde iki çağıran da siparişi "İşleniyor" görüp ikisi de iade eder.
    /// </summary>
    [Fact]
    public async Task ConcurrentCancels_ReturnTheStockOnlyOnce()
    {
        var kahve = await NewProductAsync(500);
        var orderId = PlaceOrder(new OrderLine(kahve, 7));

        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return _fixture.OrderRepo.Cancel(orderId, $"eşzamanlı {i}");
            }, TestContext.Current.CancellationToken))
            .ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);

        results.Count(r => r).Should().Be(1, "iptal yalnızca bir kez başarılı olabilir");
        (await StockOfAsync(kahve)).Should().Be(500, "stok tam olarak bir kez iade edilmeli");
    }

    [Fact]
    public async Task CancellingAnUncancellableOrder_DoesNotTouchTheStock()
    {
        var kahve = await NewProductAsync(50);
        var orderId = PlaceOrder(new OrderLine(kahve, 5));

        await using (var ctx = _fixture.DbFactory.CreateDbContext())
        {
            var code = long.Parse(orderId);
            await ctx.Orders.Where(o => o.Code == code)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, WellKnown.OrderStatuses.Delivered),
                    TestContext.Current.CancellationToken);
        }

        _fixture.OrderRepo.Cancel(orderId, "teslim edildikten sonra").Should().BeFalse();

        (await StockOfAsync(kahve)).Should().Be(45, "iptal edilemeyen sipariş stoğa dokunmamalı");
    }
}
